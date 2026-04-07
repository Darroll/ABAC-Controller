using System.Net;
using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Pap.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Api.Hosting;

/// <summary>
/// Background service that drains the webhook outbox. On each pass it:
/// 1. Reads up to <see cref="BatchSize"/> pending events whose NextAttemptAt has elapsed.
/// 2. POSTs each event's payload to its subscription's CallbackUrl with an HMAC-SHA256
///    signature over the body in the <c>X-Abac-Signature</c> header.
/// 3. Retries failed deliveries with exponential backoff; marks as DeadLetter after 3
///    failures.
///
/// The service wakes up either from a periodic poll (every <see cref="PollInterval"/>)
/// or when <see cref="WebhookDispatcherSignal"/> is signalled by a publish call.
/// </summary>
public sealed class WebhookDispatcherHostedService : BackgroundService
{
    private const int BatchSize = 32;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10)
    ];
    private const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebhookDispatcherSignal _signal;
    private readonly ILogger<WebhookDispatcherHostedService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        WebhookDispatcherSignal signal,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook dispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook dispatcher drain pass failed");
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(PollInterval);
                await _signal.Reader.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Either timeout elapsed or shutdown requested — loop runs again.
            }
        }

        _logger.LogInformation("Webhook dispatcher stopped");
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        var due = await repo.GetDueAsync(BatchSize, ct);
        if (due.Count == 0) return;

        foreach (var evt in due)
        {
            if (ct.IsCancellationRequested) break;

            var subscription = await repo.GetAsync(evt.SubscriptionId, ct);
            if (subscription is null || !subscription.Active)
            {
                await repo.UpdateEventAsync(evt with
                {
                    Status = WebhookDeliveryStatus.DeadLetter,
                    LastError = "subscription missing or inactive",
                    Attempts = evt.Attempts + 1
                }, ct);
                continue;
            }

            var (success, error) = await TryDeliverAsync(subscription, evt, ct);
            if (success)
            {
                var now = DateTimeOffset.UtcNow;
                await repo.UpdateEventAsync(evt with
                {
                    Status = WebhookDeliveryStatus.Delivered,
                    Attempts = evt.Attempts + 1,
                    DeliveredAt = now
                }, ct);

                await repo.UpsertAsync(subscription with { LastDeliveryAt = now }, ct);
                continue;
            }

            var nextAttempts = evt.Attempts + 1;
            if (nextAttempts >= MaxAttempts)
            {
                _logger.LogWarning(
                    "Webhook {EventId} → {CallbackUrl} DEAD-LETTERED after {Attempts} attempts: {Error}",
                    evt.Id, subscription.CallbackUrl, nextAttempts, error);

                await repo.UpdateEventAsync(evt with
                {
                    Status = WebhookDeliveryStatus.DeadLetter,
                    Attempts = nextAttempts,
                    LastError = error
                }, ct);
            }
            else
            {
                var backoff = RetryBackoff[Math.Min(nextAttempts, RetryBackoff.Length - 1)];
                _logger.LogInformation(
                    "Webhook {EventId} → {CallbackUrl} retry {Attempt} in {Backoff}s: {Error}",
                    evt.Id, subscription.CallbackUrl, nextAttempts, backoff.TotalSeconds, error);

                await repo.UpdateEventAsync(evt with
                {
                    Status = WebhookDeliveryStatus.Pending,
                    Attempts = nextAttempts,
                    NextAttemptAt = DateTimeOffset.UtcNow + backoff,
                    LastError = error
                }, ct);
            }
        }
    }

    private async Task<(bool Success, string? Error)> TryDeliverAsync(
        WebhookSubscription subscription,
        WebhookEvent evt,
        CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("AbacWebhookDispatcher");
            client.Timeout = HttpTimeout;

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.CallbackUrl);
            var content = new StringContent(evt.PayloadJson, Encoding.UTF8, "application/json");
            request.Content = content;

            var signature = ComputeHmacSignature(subscription.Secret, evt.PayloadJson);
            request.Headers.TryAddWithoutValidation("X-Abac-Signature", "sha256=" + signature);
            request.Headers.TryAddWithoutValidation("X-Abac-Event-Id", evt.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Abac-Event-Type", evt.EventType);

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
            {
                return (true, null);
            }

            return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (TaskCanceledException)
        {
            return (false, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return (false, "http: " + ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string ComputeHmacSignature(string secret, string body)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

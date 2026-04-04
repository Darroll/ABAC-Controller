using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Threading.Channels;
using AbacController.Api.Observability;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Api;

/// <summary>
/// A job for async evaluation with webhook callback.
/// </summary>
/// <param name="EvaluationId">Unique evaluation ID.</param>
/// <param name="Request">The evaluation request.</param>
/// <param name="CallbackUrl">Webhook URL to deliver results.</param>
/// <param name="CallbackHeaders">Optional headers for the callback.</param>
public sealed record AsyncEvaluationJob(
    string EvaluationId,
    EvaluationRequest Request,
    string CallbackUrl,
    Dictionary<string, string> CallbackHeaders);

/// <summary>
/// Thread-safe queue for async evaluation jobs with result tracking.
/// </summary>
public sealed class AsyncEvaluationQueue
{
    private readonly Channel<AsyncEvaluationJob> _channel =
        Channel.CreateBounded<AsyncEvaluationJob>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly ConcurrentDictionary<string, EvaluationResult> _completed = new();

    /// <summary>
    /// Enqueue an async evaluation job.
    /// </summary>
    public void Enqueue(AsyncEvaluationJob job)
    {
        _pending[job.EvaluationId] = 0;
        _channel.Writer.TryWrite(job);
    }

    /// <summary>
    /// Get the channel reader for consuming jobs.
    /// </summary>
    public ChannelReader<AsyncEvaluationJob> Reader => _channel.Reader;

    /// <summary>
    /// Mark a job as completed with its result.
    /// </summary>
    public void Complete(string evaluationId, EvaluationResult result)
    {
        _pending.TryRemove(evaluationId, out _);
        _completed[evaluationId] = result;

        // Evict old completed results to prevent unbounded growth
        if (_completed.Count > 10_000)
        {
            var oldest = _completed.Keys.Take(1000);
            foreach (var key in oldest)
                _completed.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Check if an evaluation is still queued/processing.
    /// </summary>
    public bool IsQueued(string evaluationId) => _pending.ContainsKey(evaluationId);

    /// <summary>
    /// Try to get a completed evaluation result.
    /// </summary>
    public bool TryGetResult(string evaluationId, out EvaluationResult? result)
        => _completed.TryGetValue(evaluationId, out result);
}

/// <summary>
/// Background service that processes async evaluation jobs and delivers results via webhook.
/// </summary>
public sealed class AsyncEvaluationService : BackgroundService
{
    private readonly AsyncEvaluationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApiMetrics _metrics;
    private readonly ILogger<AsyncEvaluationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncEvaluationService"/> class.
    /// </summary>
    public AsyncEvaluationService(
        AsyncEvaluationQueue queue,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ApiMetrics metrics,
        ILogger<AsyncEvaluationService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Async evaluation service started.");

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process async evaluation {EvaluationId}.", job.EvaluationId);

                // Mark as completed with indeterminate
                _queue.Complete(job.EvaluationId, new EvaluationResult
                {
                    DecisionId = job.EvaluationId,
                    Decision = Decision.Indeterminate,
                    Status = new StatusInfo
                    {
                        Code = "urn:oasis:names:tc:xacml:1.0:status:processing-error",
                        Message = ex.Message
                    }
                });
            }
        }
    }

    private async Task ProcessJobAsync(AsyncEvaluationJob job, CancellationToken ct)
    {
        // Evaluate
        EvaluationResult result;
        using (var scope = _scopeFactory.CreateScope())
        {
            var pdp = scope.ServiceProvider.GetRequiredService<IPdpEngine>();
            result = await pdp.EvaluateAsync(job.Request, ct);
            _metrics.RecordEvaluation(result.Decision, result.EvaluationTime);
        }

        _queue.Complete(job.EvaluationId, result);

        // Deliver via webhook
        var client = _httpClientFactory.CreateClient("AsyncEvalCallback");
        using var request = new HttpRequestMessage(HttpMethod.Post, job.CallbackUrl);

        foreach (var header in job.CallbackHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var payload = new
        {
            evaluationId = job.EvaluationId,
            decision = result.Decision == Decision.Permit,
            context = new Dictionary<string, object>
            {
                ["id"] = result.DecisionId,
                ["decision"] = result.Decision.ToString(),
                ["evaluationTime"] = result.EvaluationTime.TotalMilliseconds + "ms"
            }
        };

        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await client.SendAsync(request, ct);
            _logger.LogInformation(
                "Async evaluation {EvaluationId} delivered to {CallbackUrl} with status {StatusCode}.",
                job.EvaluationId, job.CallbackUrl, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to deliver async evaluation {EvaluationId} to {CallbackUrl}.",
                job.EvaluationId, job.CallbackUrl);
        }
    }
}

using System.Threading.Channels;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Audit;

/// <summary>
/// Buffered audit writer backed by a bounded channel.
/// When the channel is full, callers experience explicit backpressure instead of
/// silently dropping older audit records.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    internal const int DefaultCapacity = 10_000;
    internal static readonly TimeSpan DefaultEnqueueTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<AuditEvent> _channel;
    private readonly TimeSpan _enqueueTimeout;

    public AuditWriter()
        : this(DefaultCapacity, DefaultEnqueueTimeout)
    {
    }

    public AuditWriter(int capacity, TimeSpan enqueueTimeout)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        if (enqueueTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(enqueueTimeout));

        _enqueueTimeout = enqueueTimeout;
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Get the channel reader for the background writer service.</summary>
    internal ChannelReader<AuditEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public void Write(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        if (_channel.Writer.TryWrite(auditEvent))
            return;

        using var cts = new CancellationTokenSource(_enqueueTimeout);

        try
        {
            while (true)
            {
                var canWrite = _channel.Writer
                    .WaitToWriteAsync(cts.Token)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                if (!canWrite)
                {
                    throw new InvalidOperationException("The audit writer is closed and cannot accept new events.");
                }

                if (_channel.Writer.TryWrite(auditEvent))
                    return;
            }
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException(
                $"Timed out after {_enqueueTimeout.TotalSeconds:0.#} seconds waiting to enqueue an audit event.",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken ct = default)
    {
        _channel.Writer.TryComplete();
        await _channel.Reader.Completion.WaitAsync(ct);
    }
}

/// <summary>
/// Background service that reads from the audit channel and batch-writes to the database.
/// </summary>
public sealed class AuditBatchWriterService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly AuditWriter _writer;
    private readonly IServiceScopeFactory _scopeFactory;
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    public AuditBatchWriterService(IAuditWriter writer, IServiceScopeFactory scopeFactory)
    {
        _writer = (AuditWriter)writer;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditEventEntity>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(FlushInterval);

                try
                {
                    while (batch.Count < BatchSize &&
                           await _writer.Reader.WaitToReadAsync(cts.Token))
                    {
                        while (batch.Count < BatchSize &&
                               _writer.Reader.TryRead(out var auditEvent))
                        {
                            batch.Add(MapToEntity(auditEvent));
                        }
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Flush what we have on interval expiry.
                }

                if (batch.Count > 0)
                {
                    await WriteBatchAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                batch.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        while (_writer.Reader.TryRead(out var finalEvent))
            batch.Add(MapToEntity(finalEvent));

        if (batch.Count > 0)
            await WriteBatchAsync(batch, CancellationToken.None);
    }

    private async Task WriteBatchAsync(List<AuditEventEntity> batch, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbacDbContext>();
        db.AuditEvents.AddRange(batch);
        await db.SaveChangesAsync(ct);
    }

    private static AuditEventEntity MapToEntity(AuditEvent e) => new()
    {
        Id = e.Id,
        Timestamp = e.Timestamp,
        EventType = e.EventType,
        RequestId = e.RequestId,
        DecisionId = e.DecisionId,
        SubjectType = e.SubjectType,
        SubjectId = e.SubjectId,
        ActionName = e.ActionName,
        ResourceType = e.ResourceType,
        ResourceId = e.ResourceId,
        Decision = e.Decision,
        AppliedPolicies = e.AppliedPolicies,
        ObligationsJson = e.ObligationsJson,
        AttributesUsedJson = e.AttributesUsedJson,
        EvaluationTimeMs = e.EvaluationTimeMs,
        PepId = e.PepId,
        ActorIdentity = e.ActorIdentity,
        DetailJson = e.DetailJson
    };
}

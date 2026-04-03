using System.Threading.Channels;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Audit;

/// <summary>
/// Buffered audit writer using Channel&lt;T&gt; for non-blocking enqueue.
/// Events are flushed by the AuditBatchWriterService background service.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly Channel<AuditEvent> _channel;

    public AuditWriter()
    {
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Get the channel reader for the background writer service.</summary>
    internal ChannelReader<AuditEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public void Write(AuditEvent auditEvent)
    {
        // Non-blocking write. If channel is full, oldest events are dropped.
        _channel.Writer.TryWrite(auditEvent);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken ct = default)
    {
        _channel.Writer.Complete();
        // Wait for reader to drain
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
                // Wait for events or timeout
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
                    // Timeout — flush what we have
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
                // Don't let errors kill the background service
                batch.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Final drain on shutdown
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

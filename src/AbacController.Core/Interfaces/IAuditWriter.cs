using System.Threading.Channels;
using AbacController.Core.Domain.Audit;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Writes audit records. Implementation buffers and batch-writes for performance.
/// All writes are append-only — no update or delete operations.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Queue an audit event for writing.
    /// Implementations must not silently discard events when the buffer is full;
    /// they should either apply explicit backpressure or fail the write.
    /// </summary>
    void Write(AuditEvent auditEvent);

    /// <summary>
    /// Force flush all buffered audit events to the data store.
    /// Called on graceful shutdown.
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}

/// <summary>
/// Provides read access to the audit event channel for background consumption.
/// Separates the channel-reading concern from the write interface (ISP).
/// </summary>
public interface IAuditChannelReader
{
    /// <summary>Gets the channel reader for consuming buffered audit events.</summary>
    ChannelReader<AuditEvent> Reader { get; }
}

/// <summary>
/// Reads audit records with filtering and pagination.
/// </summary>
public interface IAuditReader
{
    /// <summary>Query audit events.</summary>
    Task<AuditQueryResult> QueryAsync(AuditQuery query, CancellationToken ct = default);

    /// <summary>Get a single audit event by ID.</summary>
    Task<AuditEvent?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get audit events by decision ID.</summary>
    Task<List<AuditEvent>> GetByDecisionIdAsync(string decisionId, CancellationToken ct = default);
}

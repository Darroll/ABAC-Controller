using AbacController.Core.Domain.Audit;
using AbacController.Core.Interfaces;
using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data.Repositories;

/// <summary>
/// EF Core-backed audit query repository.
/// </summary>
public sealed class AuditRepository : IAuditReader
{
    private readonly AbacDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="AuditRepository"/> class.</summary>
    public AuditRepository(AbacDbContext db) => _db = db;

    /// <summary>
    /// Queries audit events using the supplied filters and pagination settings.
    /// </summary>
    public async Task<AuditQueryResult> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var q = _db.AuditEvents.AsNoTracking().AsQueryable();

        if (query.From.HasValue)
            q = q.Where(e => e.Timestamp >= query.From.Value);
        if (query.To.HasValue)
            q = q.Where(e => e.Timestamp <= query.To.Value);
        if (!string.IsNullOrEmpty(query.SubjectId))
            q = q.Where(e => e.SubjectId == query.SubjectId);
        if (!string.IsNullOrEmpty(query.ResourceId))
            q = q.Where(e => e.ResourceId == query.ResourceId);
        if (!string.IsNullOrEmpty(query.EventType))
            q = q.Where(e => e.EventType == query.EventType);
        if (!string.IsNullOrEmpty(query.Decision))
            q = q.Where(e => e.Decision == query.Decision);

        var totalCount = await q.CountAsync(ct);

        List<AuditEventEntity> events;
        if (_db.Database.IsSqlite())
        {
            events = (await q.ToListAsync(ct))
                .OrderByDescending(static e => e.Timestamp)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();
        }
        else
        {
            events = await q
                .OrderByDescending(e => e.Timestamp)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);
        }

        return new AuditQueryResult
        {
            Events = events.Select(MapToDomain).ToList(),
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    /// <summary>
    /// Loads a single audit event by its identifier.
    /// </summary>
    public async Task<AuditEvent?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.AuditEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is null ? null : MapToDomain(entity);
    }

    /// <summary>
    /// Loads all audit events associated with the supplied decision identifier.
    /// </summary>
    public async Task<List<AuditEvent>> GetByDecisionIdAsync(string decisionId, CancellationToken ct = default)
    {
        var query = _db.AuditEvents
            .AsNoTracking()
            .Where(e => e.DecisionId == decisionId);

        List<AuditEventEntity> entities;
        if (_db.Database.IsSqlite())
        {
            entities = (await query.ToListAsync(ct))
                .OrderByDescending(static e => e.Timestamp)
                .ToList();
        }
        else
        {
            entities = await query
                .OrderByDescending(e => e.Timestamp)
                .ToListAsync(ct);
        }

        return entities.Select(MapToDomain).ToList();
    }

    private static AuditEvent MapToDomain(AuditEventEntity e) => new()
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

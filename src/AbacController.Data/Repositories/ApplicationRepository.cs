using System.Text.Json;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Interfaces;
using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IApplicationRepository"/> with multi-tenant isolation.
/// </summary>
public sealed class ApplicationRepository : IApplicationRepository
{
    private readonly AbacDbContext _db;
    private readonly ITenantContext? _tenantContext;

    public ApplicationRepository(AbacDbContext db, ITenantContext? tenantContext = null)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    private string? CurrentTenantId => _tenantContext?.TenantId;

    public async Task<List<ApplicationRegistration>> GetAllAsync(CancellationToken ct = default)
    {
        var entities = await TenantQuery()
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<ApplicationRegistration?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await TenantQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<ApplicationRegistration> UpsertAsync(ApplicationRegistration registration, CancellationToken ct = default)
    {
        var existing = await TenantQuery().FirstOrDefaultAsync(e => e.Id == registration.Id, ct);

        if (existing is null)
        {
            var entity = MapToEntity(registration);
            entity.TenantId = CurrentTenantId;
            entity.CreatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            _db.ApplicationRegistrations.Add(entity);
            await _db.SaveChangesAsync(ct);
            return MapToDomain(entity);
        }

        existing.Name = registration.Name;
        existing.Description = registration.Description;
        existing.DefaultPolicyOid = registration.DefaultPolicyOid;
        existing.AllowedClassificationLacvsJson = JsonSerializer.Serialize(registration.AllowedClassificationLacvs);
        existing.MaxClassificationHierarchy = registration.MaxClassificationHierarchy;
        existing.AllowedTagSetOidsJson = JsonSerializer.Serialize(registration.AllowedTagSetOids);
        existing.IsActive = registration.IsActive;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapToDomain(existing);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await TenantQuery().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;

        _db.ApplicationRegistrations.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<ApplicationRegistrationEntity> TenantQuery()
    {
        return _db.ApplicationRegistrations
            .Where(e => e.TenantId == CurrentTenantId || e.TenantId == null);
    }

    private static ApplicationRegistration MapToDomain(ApplicationRegistrationEntity entity)
    {
        return new ApplicationRegistration
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            DefaultPolicyOid = entity.DefaultPolicyOid,
            AllowedClassificationLacvs = DeserializeIntList(entity.AllowedClassificationLacvsJson),
            MaxClassificationHierarchy = entity.MaxClassificationHierarchy,
            AllowedTagSetOids = DeserializeStringList(entity.AllowedTagSetOidsJson),
            IsActive = entity.IsActive,
            TenantId = entity.TenantId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static ApplicationRegistrationEntity MapToEntity(ApplicationRegistration registration)
    {
        return new ApplicationRegistrationEntity
        {
            Id = registration.Id,
            Name = registration.Name,
            Description = registration.Description,
            DefaultPolicyOid = registration.DefaultPolicyOid,
            AllowedClassificationLacvsJson = JsonSerializer.Serialize(registration.AllowedClassificationLacvs),
            MaxClassificationHierarchy = registration.MaxClassificationHierarchy,
            AllowedTagSetOidsJson = JsonSerializer.Serialize(registration.AllowedTagSetOids),
            IsActive = registration.IsActive,
            TenantId = registration.TenantId,
            CreatedAt = registration.CreatedAt,
            UpdatedAt = registration.UpdatedAt
        };
    }

    private static List<int> DeserializeIntList(string json)
    {
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? []; }
        catch { return []; }
    }

    private static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}

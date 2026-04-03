using AbacController.Core.Domain.Policy;
using AbacController.Core.Interfaces;
using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data.Repositories;

/// <summary>
/// EF Core implementation of IPolicyRepository.
/// </summary>
public sealed class PolicyRepository : IPolicyRepository
{
    private readonly AbacDbContext _db;

    public PolicyRepository(AbacDbContext db) => _db = db;

    // ── Policy Sets ──

    public async Task<List<PolicySet>> GetPolicySetsAsync(CancellationToken ct = default)
    {
        var entities = await _db.PolicySets
            .Include(ps => ps.Policies)
            .ThenInclude(p => p.Versions)
            .AsNoTracking()
            .ToListAsync(ct);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.PolicySets
            .Include(ps => ps.Policies)
            .ThenInclude(p => p.Versions)
            .AsNoTracking()
            .FirstOrDefaultAsync(ps => ps.Id == id, ct);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<PolicySet> CreatePolicySetAsync(PolicySet policySet, CancellationToken ct = default)
    {
        var entity = MapToEntity(policySet);
        _db.PolicySets.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDomain(entity);
    }

    public async Task<PolicySet> UpdatePolicySetAsync(PolicySet policySet, CancellationToken ct = default)
    {
        var entity = await _db.PolicySets.FindAsync([policySet.Id], ct)
            ?? throw new InvalidOperationException($"PolicySet {policySet.Id} not found");

        entity.Name = policySet.Name;
        entity.Description = policySet.Description;
        entity.SpifId = policySet.SpifId;
        entity.CombiningAlgorithm = policySet.CombiningAlgorithm;
        entity.IsActive = policySet.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapToDomain(entity);
    }

    public async Task DeletePolicySetAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.PolicySets.FindAsync([id], ct);
        if (entity is not null)
        {
            _db.PolicySets.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Policies ──

    public async Task<Policy?> GetPolicyAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.Policies
            .Include(p => p.Versions)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return entity is null ? null : MapPolicyToDomain(entity);
    }

    public async Task<Policy> CreatePolicyAsync(Policy policy, CancellationToken ct = default)
    {
        var entity = MapPolicyToEntity(policy);
        _db.Policies.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapPolicyToDomain(entity);
    }

    public async Task<Policy> UpdatePolicyAsync(Policy policy, CancellationToken ct = default)
    {
        var entity = await _db.Policies.FindAsync([policy.Id], ct)
            ?? throw new InvalidOperationException($"Policy {policy.Id} not found");

        entity.Name = policy.Name;
        entity.Format = policy.Format;
        entity.ActiveVersionId = policy.ActiveVersionId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapPolicyToDomain(entity);
    }

    public async Task DeletePolicyAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.Policies.FindAsync([id], ct);
        if (entity is not null)
        {
            _db.Policies.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Policy Versions ──

    public async Task<List<PolicyVersion>> GetVersionsAsync(string policyId, CancellationToken ct = default)
    {
        var entities = await _db.PolicyVersions
            .Where(v => v.PolicyId == policyId)
            .OrderByDescending(v => v.VersionNumber)
            .AsNoTracking()
            .ToListAsync(ct);

        return entities.Select(MapVersionToDomain).ToList();
    }

    public async Task<PolicyVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var entity = await _db.PolicyVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);

        return entity is null ? null : MapVersionToDomain(entity);
    }

    public async Task<PolicyVersion> CreateVersionAsync(PolicyVersion version, CancellationToken ct = default)
    {
        var entity = MapVersionToEntity(version);
        _db.PolicyVersions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapVersionToDomain(entity);
    }

    public async Task ActivateVersionAsync(string policyId, Guid versionId, CancellationToken ct = default)
    {
        var policy = await _db.Policies.FindAsync([policyId], ct)
            ?? throw new InvalidOperationException($"Policy {policyId} not found");

        // Deactivate all versions
        var versions = await _db.PolicyVersions
            .Where(v => v.PolicyId == policyId)
            .ToListAsync(ct);

        foreach (var v in versions)
            v.IsActive = false;

        // Activate the target version
        var target = versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new InvalidOperationException($"Version {versionId} not found");

        target.IsActive = true;
        policy.ActiveVersionId = versionId;
        policy.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    // ── Mapping ──

    private static PolicySet MapToDomain(PolicySetEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        SpifId = e.SpifId,
        CombiningAlgorithm = e.CombiningAlgorithm,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        Policies = e.Policies.Select(MapPolicyToDomain).ToList()
    };

    private static PolicySetEntity MapToEntity(PolicySet ps) => new()
    {
        Id = ps.Id,
        Name = ps.Name,
        Description = ps.Description,
        SpifId = ps.SpifId,
        CombiningAlgorithm = ps.CombiningAlgorithm,
        IsActive = ps.IsActive,
        CreatedAt = ps.CreatedAt,
        UpdatedAt = ps.UpdatedAt
    };

    private static Policy MapPolicyToDomain(PolicyEntity e) => new()
    {
        Id = e.Id,
        PolicySetId = e.PolicySetId,
        Name = e.Name,
        Format = e.Format,
        ActiveVersionId = e.ActiveVersionId,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        Versions = e.Versions.Select(MapVersionToDomain).ToList()
    };

    private static PolicyEntity MapPolicyToEntity(Policy p) => new()
    {
        Id = p.Id,
        PolicySetId = p.PolicySetId,
        Name = p.Name,
        Format = p.Format,
        ActiveVersionId = p.ActiveVersionId,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };

    private static PolicyVersion MapVersionToDomain(PolicyVersionEntity e) => new()
    {
        Id = e.Id,
        PolicyId = e.PolicyId,
        VersionNumber = e.VersionNumber,
        Content = e.Content,
        Hash = e.Hash,
        CreatedAt = e.CreatedAt,
        CreatedBy = e.CreatedBy,
        IsActive = e.IsActive
    };

    private static PolicyVersionEntity MapVersionToEntity(PolicyVersion v) => new()
    {
        Id = v.Id,
        PolicyId = v.PolicyId,
        VersionNumber = v.VersionNumber,
        Content = v.Content,
        Hash = v.Hash,
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy,
        IsActive = v.IsActive
    };
}

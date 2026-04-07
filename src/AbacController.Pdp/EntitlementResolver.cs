using System.Collections.Immutable;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// Resolves a subject's effective entitlement set by combining tenant baseline,
/// directory-group grants and per-user overrides.
///
/// Effective formula: <c>baseline ∪ group_grants ∪ user_grants</c>, with user denies
/// applied later at the filter site via <see cref="ResolvedEntitlements.Permits"/>.
/// Denies are kept separate from grants so the filter layer can apply deny-wins
/// semantics per classification without needing the full SPIF classification list here.
/// </summary>
public sealed class EntitlementResolver : IEntitlementResolver
{
    private readonly IEntitlementRepository _repository;

    public EntitlementResolver(IEntitlementRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResolvedEntitlements> ResolveAsync(EntitlementSubject subject, CancellationToken ct = default)
    {
        var tenantId = subject.TenantId ?? string.Empty;
        var trace = new List<string>();

        var grants = new List<EntitlementGrant>();

        var baseline = await _repository.GetBaselineAsync(tenantId, ct);
        grants.AddRange(baseline);
        trace.Add($"baseline:{baseline.Count}");

        var groupGrants = await _repository.GetAllGroupEntitlementsAsync(tenantId, subject.GroupIds, ct);
        grants.AddRange(groupGrants);
        trace.Add($"group:{groupGrants.Count}");

        var (userGrants, userDenies) = await _repository.GetUserOverridesAsync(tenantId, subject.SubjectId, ct);
        grants.AddRange(userGrants);
        trace.Add($"user_grant:{userGrants.Count}");
        trace.Add($"user_deny:{userDenies.Count}");

        var policyOids = ImmutableHashSet.CreateBuilder<string>();
        var byPolicy = new Dictionary<string, ImmutableHashSet<int>.Builder>();
        var tagSetOids = ImmutableHashSet.CreateBuilder<string>();

        foreach (var grant in grants)
        {
            if (grant.ClassificationLacv is null)
            {
                policyOids.Add(grant.PolicyOid);
            }
            else
            {
                if (!byPolicy.TryGetValue(grant.PolicyOid, out var set))
                {
                    set = ImmutableHashSet.CreateBuilder<int>();
                    byPolicy[grant.PolicyOid] = set;
                }
                set.Add(grant.ClassificationLacv.Value);
            }

            if (!string.IsNullOrWhiteSpace(grant.TagSetOid))
                tagSetOids.Add(grant.TagSetOid!);
        }

        var deniedPolicyOids = ImmutableHashSet.CreateBuilder<string>();
        var deniedByPolicy = new Dictionary<string, ImmutableHashSet<int>.Builder>();

        foreach (var deny in userDenies)
        {
            if (deny.ClassificationLacv is null)
            {
                deniedPolicyOids.Add(deny.PolicyOid);
            }
            else
            {
                if (!deniedByPolicy.TryGetValue(deny.PolicyOid, out var set))
                {
                    set = ImmutableHashSet.CreateBuilder<int>();
                    deniedByPolicy[deny.PolicyOid] = set;
                }
                set.Add(deny.ClassificationLacv.Value);
            }
        }

        return new ResolvedEntitlements
        {
            PolicyOids = policyOids.ToImmutable(),
            ClassificationLacvsByPolicy = byPolicy.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutable()),
            DeniedPolicyOids = deniedPolicyOids.ToImmutable(),
            DeniedClassificationLacvsByPolicy = deniedByPolicy.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutable()),
            TagSetOids = tagSetOids.ToImmutable(),
            TraceNotes = trace
        };
    }
}

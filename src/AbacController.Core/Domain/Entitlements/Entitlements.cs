using System.Collections.Immutable;

namespace AbacController.Core.Domain.Entitlements;

/// <summary>
/// Scope at which an entitlement grant is attached.
/// </summary>
public enum EntitlementScope
{
    /// <summary>Tenant-wide baseline grant, applies to every subject in the tenant.</summary>
    Baseline = 0,

    /// <summary>Grant attached to a directory group (Entra group id, LDAP DN, etc.).</summary>
    Group = 1,

    /// <summary>Grant or deny attached to a specific user subject.</summary>
    User = 2
}

/// <summary>
/// A positive entitlement — permission to use a SPIF policy, or a specific
/// classification within it, and optionally a specific category tag set.
/// </summary>
public sealed record EntitlementGrant
{
    /// <summary>Tenant this grant belongs to (null = system-wide).</summary>
    public string? TenantId { get; init; }

    /// <summary>Whether this is a baseline, group, or user grant.</summary>
    public required EntitlementScope Scope { get; init; }

    /// <summary>
    /// The target subject for the grant: group id (Scope=Group), user id (Scope=User),
    /// or null (Scope=Baseline).
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>The SPIF policy OID this grant covers.</summary>
    public required string PolicyOid { get; init; }

    /// <summary>
    /// Classification LACV this grant covers. Null means the grant applies to every
    /// classification in the policy.
    /// </summary>
    public int? ClassificationLacv { get; init; }

    /// <summary>
    /// Optional category tag set OID this grant covers. Null means no tag-set restriction.
    /// </summary>
    public string? TagSetOid { get; init; }
}

/// <summary>
/// A per-user denial of a policy or classification. Denies take precedence over grants.
/// </summary>
public sealed record EntitlementDeny
{
    /// <summary>Tenant this deny belongs to.</summary>
    public string? TenantId { get; init; }

    /// <summary>User subject id this deny targets.</summary>
    public required string UserId { get; init; }

    /// <summary>SPIF policy OID being denied.</summary>
    public required string PolicyOid { get; init; }

    /// <summary>Classification LACV being denied (null = whole policy).</summary>
    public int? ClassificationLacv { get; init; }

    /// <summary>Optional tag set OID being denied.</summary>
    public string? TagSetOid { get; init; }

    /// <summary>Optional free-text reason for audit.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// The input shape for entitlement resolution — who the subject is, what tenant they
/// belong to, and which directory groups they are a member of.
/// </summary>
public sealed record EntitlementSubject
{
    /// <summary>Subject identifier (user id).</summary>
    public required string SubjectId { get; init; }

    /// <summary>Tenant scope.</summary>
    public string? TenantId { get; init; }

    /// <summary>Group identifiers the subject is a member of.</summary>
    public IReadOnlyList<string> GroupIds { get; init; } = [];
}

/// <summary>
/// The resolved effective entitlement set for a subject. Grants and denies are both
/// retained so the filter layer can apply deny-wins semantics per-classification without
/// needing to know the full SPIF classification list at resolve time.
/// </summary>
public sealed record ResolvedEntitlements
{
    /// <summary>
    /// Policy OIDs the subject has whole-policy access to (a baseline/group/user grant
    /// with ClassificationLacv == null).
    /// </summary>
    public ImmutableHashSet<string> PolicyOids { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// For each policy, the set of classification LACVs the subject has a classification-
    /// scoped grant for. Absent key = no scoped grants on that policy.
    /// </summary>
    public ImmutableDictionary<string, ImmutableHashSet<int>> ClassificationLacvsByPolicy { get; init; }
        = ImmutableDictionary<string, ImmutableHashSet<int>>.Empty;

    /// <summary>
    /// Policy OIDs that have been denied at the whole-policy level for this user.
    /// </summary>
    public ImmutableHashSet<string> DeniedPolicyOids { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// For each policy, the set of classification LACVs denied at the classification level.
    /// </summary>
    public ImmutableDictionary<string, ImmutableHashSet<int>> DeniedClassificationLacvsByPolicy { get; init; }
        = ImmutableDictionary<string, ImmutableHashSet<int>>.Empty;

    /// <summary>Tag set OIDs the subject is entitled to use across all policies.</summary>
    public ImmutableHashSet<string> TagSetOids { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>Source-by-source trace entries useful for debugging/trace UIs.</summary>
    public IReadOnlyList<string> TraceNotes { get; init; } = [];

    /// <summary>
    /// Returns true when the subject is entitled to use <paramref name="classificationLacv"/>
    /// under <paramref name="policyOid"/>. Deny rules apply first: a whole-policy deny,
    /// or a classification-level deny for the specific LACV, blocks access. Otherwise a
    /// whole-policy grant or a matching classification-level grant permits access.
    /// </summary>
    public bool Permits(string policyOid, int classificationLacv)
    {
        if (DeniedPolicyOids.Contains(policyOid))
            return false;

        if (DeniedClassificationLacvsByPolicy.TryGetValue(policyOid, out var deniedLacvs)
            && deniedLacvs.Contains(classificationLacv))
            return false;

        if (PolicyOids.Contains(policyOid))
            return true;

        return ClassificationLacvsByPolicy.TryGetValue(policyOid, out var lacvs)
               && lacvs.Contains(classificationLacv);
    }

    /// <summary>True when the resolved set has no grants at all.</summary>
    public bool IsEmpty => PolicyOids.IsEmpty && ClassificationLacvsByPolicy.IsEmpty;
}

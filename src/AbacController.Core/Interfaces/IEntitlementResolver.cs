using AbacController.Core.Domain.Entitlements;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Resolves a subject's effective entitlement set by combining tenant baseline grants,
/// group grants and user grant/deny overrides.
/// Effective formula: <c>baseline ∪ group_grants ∪ user_grants − user_denies</c>.
/// </summary>
public interface IEntitlementResolver
{
    /// <summary>
    /// Compute the resolved entitlement set for the given subject.
    /// </summary>
    Task<ResolvedEntitlements> ResolveAsync(EntitlementSubject subject, CancellationToken ct = default);
}

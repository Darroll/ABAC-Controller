using AbacController.Core.Domain.Policy;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Policy CRUD operations with versioning support.
/// </summary>
public interface IPolicyRepository
{
    // ── Policy Sets ──

    /// <summary>Get all policy sets.</summary>
    Task<List<PolicySet>> GetPolicySetsAsync(CancellationToken ct = default);

    /// <summary>Get a policy set by ID.</summary>
    Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken ct = default);

    /// <summary>Create a policy set.</summary>
    Task<PolicySet> CreatePolicySetAsync(PolicySet policySet, CancellationToken ct = default);

    /// <summary>Update a policy set.</summary>
    Task<PolicySet> UpdatePolicySetAsync(PolicySet policySet, CancellationToken ct = default);

    /// <summary>Delete a policy set.</summary>
    Task DeletePolicySetAsync(string id, CancellationToken ct = default);

    // ── Policies ──

    /// <summary>Get a policy by ID.</summary>
    Task<Policy?> GetPolicyAsync(string id, CancellationToken ct = default);

    /// <summary>Create a policy.</summary>
    Task<Policy> CreatePolicyAsync(Policy policy, CancellationToken ct = default);

    /// <summary>Update a policy.</summary>
    Task<Policy> UpdatePolicyAsync(Policy policy, CancellationToken ct = default);

    /// <summary>Delete a policy.</summary>
    Task DeletePolicyAsync(string id, CancellationToken ct = default);

    // ── Policy Versions ──

    /// <summary>Get all versions of a policy.</summary>
    Task<List<PolicyVersion>> GetVersionsAsync(string policyId, CancellationToken ct = default);

    /// <summary>Get a specific version.</summary>
    Task<PolicyVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>Create a new version of a policy.</summary>
    Task<PolicyVersion> CreateVersionAsync(PolicyVersion version, CancellationToken ct = default);

    /// <summary>Activate a specific version of a policy.</summary>
    Task ActivateVersionAsync(string policyId, Guid versionId, CancellationToken ct = default);
}

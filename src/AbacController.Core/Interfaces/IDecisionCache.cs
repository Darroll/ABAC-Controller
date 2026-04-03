using AbacController.Core.Domain.Decisions;

namespace AbacController.Core.Interfaces;

/// <summary>
/// PDP decision cache for caching evaluation results.
/// </summary>
public interface IDecisionCache
{
    /// <summary>Try to get a cached decision result.</summary>
    bool TryGet(string cacheKey, out EvaluationResult? result);

    /// <summary>Cache a decision result.</summary>
    void Set(string cacheKey, EvaluationResult result, TimeSpan ttl);

    /// <summary>Compute a cache key for an evaluation request.</summary>
    string ComputeKey(EvaluationRequest request, string policyVersion);

    /// <summary>Invalidate cached decisions for a policy set.</summary>
    void InvalidateByPolicySet(string policySetId);

    /// <summary>Invalidate all cached decisions.</summary>
    void InvalidateAll();
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// In-memory decision cache backed by MemoryCache.
/// Thread-safe, shared across requests.
/// </summary>
public sealed class DecisionCache : IDecisionCache
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });

    /// <inheritdoc />
    public bool TryGet(string cacheKey, out EvaluationResult? result)
    {
        return _cache.TryGetValue(cacheKey, out result);
    }

    /// <inheritdoc />
    public void Set(string cacheKey, EvaluationResult result, TimeSpan ttl)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = 1
        };
        _cache.Set(cacheKey, result, options);
    }

    /// <inheritdoc />
    public string ComputeKey(EvaluationRequest request, string policyVersion)
    {
        var input = $"{request.Subject.Id}|{request.Action.Name}|" +
                    $"{request.Resource.Type}|{request.Resource.Id}|" +
                    $"{request.Options.PolicySetId}|{policyVersion}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    /// <inheritdoc />
    public void InvalidateByPolicySet(string policySetId)
    {
        // MemoryCache doesn't support tag-based invalidation.
        // For v1, we compact the entire cache when a policy changes.
        _cache.Compact(1.0);
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        _cache.Compact(1.0);
    }
}

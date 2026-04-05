using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace AbacController.Pdp;

/// <summary>
/// In-memory decision cache backed by IMemoryCache.
/// Thread-safe, shared across requests.
/// </summary>
public sealed class DecisionCache : IDecisionCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _policyKeys = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionCache"/> class.
    /// </summary>
    public DecisionCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public bool TryGet(string cacheKey, out EvaluationResult? result)
        => _cache.TryGetValue(cacheKey, out result);

    /// <inheritdoc />
    public void Set(string cacheKey, EvaluationResult result, TimeSpan ttl)
    {
        using var entry = _cache.CreateEntry(cacheKey);
        entry.Value = result;
        entry.AbsoluteExpirationRelativeToNow = ttl;

        foreach (var policySetId in result.AppliedPolicies
                     .Where(static p => p.StartsWith("policy-set:", StringComparison.Ordinal)))
        {
            var policyIndex = _policyKeys.GetOrAdd(policySetId, _ => new ConcurrentDictionary<string, byte>());
            policyIndex[cacheKey] = 0;

            entry.RegisterPostEvictionCallback(static (key, _, _, state) =>
            {
                if (key is not string evictedKey || state is not PolicyEntryState callbackState)
                {
                    return;
                }

                if (callbackState.PolicyKeys.TryGetValue(callbackState.PolicySetId, out var indexedKeys))
                {
                    indexedKeys.TryRemove(evictedKey, out _);
                    if (indexedKeys.IsEmpty)
                    {
                        callbackState.PolicyKeys.TryRemove(callbackState.PolicySetId, out _);
                    }
                }
            }, new PolicyEntryState(policySetId, _policyKeys));
        }
    }

    /// <inheritdoc />
    public string ComputeKey(EvaluationRequest request, string policyVersion)
    {
        var input = string.Join("|",
            request.Subject.Id,
            request.Action.Name,
            request.Resource.Type,
            request.Resource.Id,
            request.Options.PolicySetId ?? "-",
            policyVersion);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    /// <inheritdoc />
    public void InvalidateByPolicySet(string policySetId)
    {
        var normalizedKey = policySetId.StartsWith("policy-set:", StringComparison.Ordinal)
            ? policySetId
            : $"policy-set:{policySetId}";

        if (!_policyKeys.TryRemove(normalizedKey, out var keys))
        {
            return;
        }

        foreach (var cacheKey in keys.Keys)
        {
            _cache.Remove(cacheKey);
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        foreach (var policySetId in _policyKeys.Keys.ToList())
        {
            InvalidateByPolicySet(policySetId);
        }
    }

    private sealed record PolicyEntryState(
        string PolicySetId,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> PolicyKeys);
}

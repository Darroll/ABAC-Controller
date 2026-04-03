using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
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

        foreach (var policySetId in result.AppliedPolicies.Where(static p => !string.IsNullOrWhiteSpace(p)))
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
        var label = request.Resource.Properties.TryGetValue("securityLabel", out var labelObj) && labelObj is SecurityLabel securityLabel
            ? SerializeLabel(securityLabel)
            : "-";

        var clearance = request.Subject.Properties.TryGetValue("securityClearance", out var clearanceObj) && clearanceObj is SecurityClearance securityClearance
            ? SerializeClearance(securityClearance)
            : "-";

        var input = string.Join("|",
            request.Subject.Type,
            request.Subject.Id,
            request.Action.Name,
            request.Resource.Type,
            request.Resource.Id,
            request.Options.PolicySetId ?? "-",
            request.Options.PolicyIdOverride ?? "-",
            policyVersion,
            label,
            clearance);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    /// <inheritdoc />
    public void InvalidateByPolicySet(string policySetId)
    {
        if (!_policyKeys.TryRemove(policySetId, out var keys))
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

    private static string SerializeLabel(SecurityLabel label)
    {
        var sb = new StringBuilder();
        sb.Append(label.PolicyOid ?? "-")
            .Append(':').Append(label.ClassificationLacv.Value);

        foreach (var tagSet in label.CategoryTagSets.OrderBy(static t => t.TagSetOid, StringComparer.Ordinal))
        {
            sb.Append('|').Append(tagSet.TagSetOid);
            foreach (var tag in tagSet.Tags.OrderBy(static t => t.TagOid ?? t.Name ?? string.Empty, StringComparer.Ordinal))
            {
                sb.Append('[')
                    .Append(tag.TagOid ?? tag.Name ?? tag.TagType.ToString())
                    .Append(':').Append(tag.TagType);

                if (tag.EnumType is not null)
                {
                    sb.Append(':').Append(tag.EnumType.Value);
                }

                var values = tag.TagType == Core.Domain.Spif.TagType.Enumerated
                    ? tag.EnumeratedValues.OrderBy(static v => v.Value)
                    : tag.Bits.OrderBy(static v => v.Value);

                foreach (var value in values)
                {
                    sb.Append(',').Append(value.Value);
                }

                sb.Append(']');
            }
        }

        return sb.ToString();
    }

    private static string SerializeClearance(SecurityClearance clearance)
    {
        var sb = new StringBuilder();
        sb.Append(clearance.PolicyOid);

        foreach (var classification in clearance.ClassificationLacvs.OrderBy(static c => c.Value))
        {
            sb.Append(':').Append(classification.Value);
        }

        foreach (var tagSet in clearance.CategoryTagSets.OrderBy(static t => t.TagSetOid, StringComparer.Ordinal))
        {
            sb.Append('|').Append(tagSet.TagSetOid);
            foreach (var tag in tagSet.Tags.OrderBy(static t => t.TagOid ?? string.Empty, StringComparer.Ordinal))
            {
                sb.Append('[')
                    .Append(tag.TagOid ?? tag.TagType.ToString())
                    .Append(':').Append(tag.TagType);

                var values = tag.TagType == Core.Domain.Spif.TagType.Enumerated
                    ? tag.EnumeratedValues.OrderBy(static v => v.Value)
                    : tag.Bits.OrderBy(static v => v.Value);

                foreach (var value in values)
                {
                    sb.Append(',').Append(value.Value);
                }

                sb.Append(']');
            }
        }

        return sb.ToString();
    }

    private sealed record PolicyEntryState(
        string PolicySetId,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> PolicyKeys);
}

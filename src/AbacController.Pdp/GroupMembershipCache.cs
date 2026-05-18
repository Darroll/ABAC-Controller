using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// In-memory <see cref="IGroupMembershipCache"/> backed by a concurrent dictionary
/// with a short TTL. The cache key is a string of the form
/// <c>{tenantId}|{userId}|{sha256(sortedKeycloakGroupIds)}</c> so lookups are O(1)
/// and the hash makes the key length bounded regardless of how many Keycloak
/// groups the subject is a member of.
/// </summary>
public sealed class GroupMembershipCache : IGroupMembershipCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;

    public GroupMembershipCache() : this(DefaultTtl) { }

    /// <summary>Create with an explicit TTL — used by tests.</summary>
    public GroupMembershipCache(TimeSpan ttl)
    {
        _ttl = ttl;
    }

    public bool TryGet(string tenantId, string userId, IReadOnlyCollection<string> keycloakGroupIds, out IReadOnlyCollection<Guid> abacGroupIds)
    {
        var key = BuildKey(tenantId, userId, keycloakGroupIds);
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            abacGroupIds = entry.AbacGroupIds;
            return true;
        }

        if (entry is not null)
        {
            // Drop expired entries lazily so the dictionary doesn't grow unbounded
            // for one-off subjects.
            _entries.TryRemove(key, out _);
        }

        abacGroupIds = Array.Empty<Guid>();
        return false;
    }

    public void Set(string tenantId, string userId, IReadOnlyCollection<string> keycloakGroupIds, IReadOnlyCollection<Guid> abacGroupIds)
    {
        var key = BuildKey(tenantId, userId, keycloakGroupIds);
        _entries[key] = new Entry(abacGroupIds, DateTimeOffset.UtcNow + _ttl);
    }

    public void InvalidateTenant(string tenantId)
    {
        var prefix = tenantId + "|";
        foreach (var key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Public for resolver use and testing. Hashes the sorted Keycloak group ids
    /// so the cache key is stable across claim ordering — Alice's groups
    /// {nato, ops} and {ops, nato} produce the same key.
    /// </summary>
    public static string BuildKey(string tenantId, string userId, IReadOnlyCollection<string> keycloakGroupIds)
    {
        var sorted = keycloakGroupIds
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .OrderBy(g => g, StringComparer.Ordinal)
            .ToArray();

        var joined = string.Join("\n", sorted);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        var hex = Convert.ToHexString(hash);

        return $"{tenantId}|{userId}|{hex}";
    }

    private sealed record Entry(IReadOnlyCollection<Guid> AbacGroupIds, DateTimeOffset ExpiresAt);
}

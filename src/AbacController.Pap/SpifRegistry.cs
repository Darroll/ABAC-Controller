using System.Collections.Concurrent;
using AbacController.Core.Interfaces;

namespace AbacController.Pap;

/// <summary>
/// In-memory SPIF registry. Holds active SPIFs as compiled SpifIndex objects.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public sealed class SpifRegistry : ISpifRegistry
{
    private readonly ConcurrentDictionary<string, ISpifIndex> _indexes = new();
    private volatile string? _defaultPolicyOid;

    /// <inheritdoc />
    public ISpifIndex? GetByPolicyOid(string policyOid)
        => _indexes.GetValueOrDefault(policyOid);

    /// <inheritdoc />
    public ISpifIndex? GetDefault()
    {
        if (_defaultPolicyOid is not null && _indexes.TryGetValue(_defaultPolicyOid, out var index))
            return index;

        // If no default set, return first registered
        return _indexes.Values.FirstOrDefault();
    }

    /// <inheritdoc />
    public void Register(ISpifIndex spifIndex)
    {
        _indexes[spifIndex.PolicyOid] = spifIndex;

        // Auto-set default if this is the first registration
        _defaultPolicyOid ??= spifIndex.PolicyOid;
    }

    /// <inheritdoc />
    public void SetDefault(string policyOid)
    {
        if (!_indexes.ContainsKey(policyOid))
            throw new InvalidOperationException($"Policy OID {policyOid} is not registered");
        _defaultPolicyOid = policyOid;
    }

    /// <inheritdoc />
    public void Remove(string policyOid)
    {
        _indexes.TryRemove(policyOid, out _);
        if (_defaultPolicyOid == policyOid)
            _defaultPolicyOid = _indexes.Keys.FirstOrDefault();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredPolicyOids()
        => _indexes.Keys.ToList().AsReadOnly();

    /// <inheritdoc />
    public bool IsRegistered(string policyOid)
        => _indexes.ContainsKey(policyOid);
}

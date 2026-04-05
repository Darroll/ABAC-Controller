using System.Collections.Immutable;
using AbacController.Core.Interfaces;

namespace AbacController.Pap;

/// <summary>
/// In-memory SPIF registry that publishes immutable snapshots for lock-free reads.
/// Registration and default-selection updates are serialized through a private gate.
/// </summary>
public sealed class SpifRegistry : ISpifRegistry
{
    private readonly Lock _gate = new();
    private ImmutableDictionary<string, ISpifIndex> _indexes = ImmutableDictionary<string, ISpifIndex>.Empty;
    private string? _defaultPolicyOid;

    /// <inheritdoc />
    public ISpifIndex? GetByPolicyOid(string policyOid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyOid);
        return Volatile.Read(ref _indexes).GetValueOrDefault(policyOid);
    }

    /// <inheritdoc />
    public ISpifIndex? GetDefault()
    {
        var indexes = Volatile.Read(ref _indexes);
        var defaultPolicyOid = Volatile.Read(ref _defaultPolicyOid);

        if (defaultPolicyOid is not null && indexes.TryGetValue(defaultPolicyOid, out var defaultIndex))
        {
            return defaultIndex;
        }

        return indexes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).FirstOrDefault();
    }

    /// <inheritdoc />
    public void Register(ISpifIndex spifIndex)
    {
        ArgumentNullException.ThrowIfNull(spifIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(spifIndex.PolicyOid);

        lock (_gate)
        {
            _indexes = _indexes.SetItem(spifIndex.PolicyOid, spifIndex);

            if (_defaultPolicyOid is null)
            {
                _defaultPolicyOid = spifIndex.PolicyOid;
            }
        }
    }

    /// <inheritdoc />
    public void SetDefault(string policyOid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyOid);

        lock (_gate)
        {
            if (!_indexes.ContainsKey(policyOid))
            {
                throw new InvalidOperationException($"Policy OID {policyOid} is not registered");
            }

            _defaultPolicyOid = policyOid;
        }
    }

    /// <inheritdoc />
    public void Remove(string policyOid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyOid);

        lock (_gate)
        {
            if (!_indexes.ContainsKey(policyOid))
            {
                return;
            }

            _indexes = _indexes.Remove(policyOid);

            if (string.Equals(_defaultPolicyOid, policyOid, StringComparison.Ordinal))
            {
                _defaultPolicyOid = _indexes.Keys.OrderBy(key => key, StringComparer.Ordinal).FirstOrDefault();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredPolicyOids()
        => Volatile.Read(ref _indexes)
            .Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <inheritdoc />
    public bool IsRegistered(string policyOid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyOid);
        return Volatile.Read(ref _indexes).ContainsKey(policyOid);
    }
}

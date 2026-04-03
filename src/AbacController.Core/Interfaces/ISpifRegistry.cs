namespace AbacController.Core.Interfaces;

/// <summary>
/// SPIF storage and lookup. Holds active SPIFs in memory as compiled SpifIndex objects.
/// </summary>
public interface ISpifRegistry
{
    /// <summary>Get the SpifIndex for a policy OID. Returns null if not loaded.</summary>
    ISpifIndex? GetByPolicyOid(string policyOid);

    /// <summary>Get the system default SpifIndex.</summary>
    ISpifIndex? GetDefault();

    /// <summary>Register a SpifIndex (called on SPIF import/activation).</summary>
    void Register(ISpifIndex spifIndex);

    /// <summary>Set the default SPIF by policy OID.</summary>
    void SetDefault(string policyOid);

    /// <summary>Remove a SPIF by policy OID.</summary>
    void Remove(string policyOid);

    /// <summary>Get all registered policy OIDs.</summary>
    IReadOnlyList<string> GetRegisteredPolicyOids();

    /// <summary>Check if a policy OID is registered.</summary>
    bool IsRegistered(string policyOid);
}

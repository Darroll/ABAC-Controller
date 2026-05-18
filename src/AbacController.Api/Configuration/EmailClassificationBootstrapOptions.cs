namespace AbacController.Api.Configuration;

/// <summary>
/// Optional startup bootstrap for the Email Classification application registration.
/// When <see cref="Enabled"/> is true, ABAC Controller upserts a single
/// <c>ApplicationRegistration</c> row during startup with the values below so the
/// Email Classification API has a ready-to-use application scope on first boot.
///
/// Off by default — this is a convenience for development and greenfield
/// deployments; production environments should register applications explicitly via
/// <c>PUT /pap/api/applications/{id}</c>.
/// </summary>
public sealed class EmailClassificationBootstrapOptions
{
    /// <summary>Whether to upsert the application registration on startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>Application identifier.</summary>
    public string ApplicationId { get; set; } = "email-classification";

    /// <summary>Display name.</summary>
    public string ApplicationName { get; set; } = "Email Classification";

    /// <summary>Description.</summary>
    public string? ApplicationDescription { get; set; }

    /// <summary>Default SPIF policy OID (empty means "fall back to tenant default").</summary>
    public string? DefaultPolicyOid { get; set; }

    /// <summary>Whitelist of classification LACVs permitted for this app.</summary>
    public List<int> AllowedClassificationLacvs { get; set; } = [];

    /// <summary>Optional hierarchy ceiling.</summary>
    public int? MaxClassificationHierarchy { get; set; }

    /// <summary>Whitelist of tag set OIDs permitted for this app.</summary>
    public List<string> AllowedTagSetOids { get; set; } = [];
}

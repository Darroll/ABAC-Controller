namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for a registered enforcement point.
/// </summary>
public class EnforcementPointEntity
{
    /// <summary>Gets or sets the id.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the type.</summary>
    public string Type { get; set; } = "";
    /// <summary>Gets or sets the endpoint.</summary>
    public string? Endpoint { get; set; }
    /// <summary>Gets or sets the enforcement Mode.</summary>
    public string EnforcementMode { get; set; } = "enforcing";
    /// <summary>Gets or sets the policy Set Bindings.</summary>
    public string? PolicySetBindings { get; set; }
    /// <summary>Gets or sets the spif Id.</summary>
    public string? SpifId { get; set; }
    /// <summary>Gets or sets the created At.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Gets or sets the updated At.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

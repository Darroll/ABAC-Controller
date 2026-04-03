namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for a registered enforcement point.
/// </summary>
public class EnforcementPointEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Endpoint { get; set; }
    public string EnforcementMode { get; set; } = "enforcing";
    public string? PolicySetBindings { get; set; }
    public string? SpifId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

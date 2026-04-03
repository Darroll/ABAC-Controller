namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for a policy set.
/// </summary>
public class PolicySetEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? SpifId { get; set; }
    public string CombiningAlgorithm { get; set; } = "deny-overrides";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public SpifEntity? Spif { get; set; }
    public List<PolicyEntity> Policies { get; set; } = [];
}

/// <summary>
/// EF Core entity for a policy.
/// </summary>
public class PolicyEntity
{
    public string Id { get; set; } = "";
    public string PolicySetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Format { get; set; } = "native";
    public Guid? ActiveVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public PolicySetEntity? PolicySet { get; set; }
    public List<PolicyVersionEntity> Versions { get; set; } = [];
}

/// <summary>
/// EF Core entity for a policy version (immutable snapshot).
/// </summary>
public class PolicyVersionEntity
{
    public Guid Id { get; set; }
    public string PolicyId { get; set; } = "";
    public int VersionNumber { get; set; }
    public string Content { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public bool IsActive { get; set; }

    // Navigation
    public PolicyEntity? Policy { get; set; }
}

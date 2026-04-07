namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for application registration storage.
/// </summary>
public class ApplicationRegistrationEntity
{
    /// <summary>Unique application identifier (e.g., "email-classification").</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable application name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Default SPIF policy OID for this application.</summary>
    public string? DefaultPolicyOid { get; set; }

    /// <summary>JSON-serialized list of allowed classification LACV values.</summary>
    public string AllowedClassificationLacvsJson { get; set; } = "[]";

    /// <summary>Maximum classification hierarchy value (null = no ceiling).</summary>
    public int? MaxClassificationHierarchy { get; set; }

    /// <summary>JSON-serialized list of allowed category tag set OIDs.</summary>
    public string AllowedTagSetOidsJson { get; set; } = "[]";

    /// <summary>Whether this registration is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Tenant identifier for multi-tenant isolation.</summary>
    public string? TenantId { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

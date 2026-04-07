namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for an ABAC Controller group. See
/// <see cref="AbacController.Core.Domain.Groups.AbacGroup"/> for the domain shape.
/// </summary>
public class AbacGroupEntity
{
    /// <summary>Stable group identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant scope (null = system-wide).</summary>
    public string? TenantId { get; set; }

    /// <summary>Human-readable name. Unique per tenant.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// EF Core entity for one membership row attaching a member id to an ABAC group.
/// Composite primary key: <c>(AbacGroupId, Kind, MemberId)</c>.
/// </summary>
public class AbacGroupMembershipEntity
{
    /// <summary>The owning ABAC group.</summary>
    public Guid AbacGroupId { get; set; }

    /// <summary>
    /// Kind discriminator: 0 = User (direct subject id), 1 = KeycloakGroup
    /// (inherited via external group membership).
    /// </summary>
    public int Kind { get; set; }

    /// <summary>Opaque member id whose interpretation depends on <see cref="Kind"/>.</summary>
    public string MemberId { get; set; } = "";

    /// <summary>
    /// Tenant scope, denormalised from the parent group so the resolver's covering
    /// index can satisfy a single-table lookup without a join.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

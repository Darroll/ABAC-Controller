namespace AbacController.Core.Domain.Groups;

/// <summary>
/// A first-class ABAC Controller group. Distinct from any external directory group
/// (Keycloak, AD, LDAP); ABAC groups have their own identity and can be assigned
/// SPIF entitlements directly. Membership is a mix of direct user references and
/// inherited references to external (Keycloak) groups — see
/// <see cref="AbacGroupMembership"/>.
/// </summary>
public sealed record AbacGroup
{
    /// <summary>Stable group identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Tenant scope. Null indicates a system-wide group visible to every tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Human-readable group name. Unique per tenant.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Discriminator describing the source of an <see cref="AbacGroupMembership.MemberId"/>.
/// </summary>
public enum AbacGroupMemberKind
{
    /// <summary>
    /// A direct user identifier (Keycloak <c>sub</c>, Entra object id, etc.). When
    /// the resolver sees this kind it matches against the requesting subject id.
    /// </summary>
    User = 0,

    /// <summary>
    /// A reference to an external (Keycloak) group id. Every member of that
    /// external group is treated as a member of this ABAC group at runtime.
    /// </summary>
    KeycloakGroup = 1,
}

/// <summary>
/// One membership row attaching a member id (a user id or an external group id)
/// to an ABAC group. Composite primary key: (AbacGroupId, Kind, MemberId).
/// </summary>
public sealed record AbacGroupMembership
{
    /// <summary>The ABAC group this row belongs to.</summary>
    public required Guid AbacGroupId { get; init; }

    /// <summary>Whether <see cref="MemberId"/> is a user id or a Keycloak group id.</summary>
    public required AbacGroupMemberKind Kind { get; init; }

    /// <summary>Member identifier — interpretation depends on <see cref="Kind"/>.</summary>
    public required string MemberId { get; init; }

    /// <summary>Tenant scope, copied from the parent ABAC group for index efficiency.</summary>
    public string? TenantId { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

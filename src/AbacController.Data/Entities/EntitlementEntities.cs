namespace AbacController.Data.Entities;

/// <summary>
/// Tenant-wide baseline entitlement — every subject in the tenant inherits this grant
/// unless a user-level deny overrides it.
/// </summary>
public class TenantBaselineEntitlementEntity
{
    /// <summary>Surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Tenant identifier (null = system-wide).</summary>
    public string? TenantId { get; set; }

    /// <summary>SPIF policy OID covered by this grant.</summary>
    public string PolicyOid { get; set; } = "";

    /// <summary>
    /// Specific classification LACV (null means the grant covers the whole policy).
    /// </summary>
    public int? ClassificationLacv { get; set; }

    /// <summary>Optional category tag set OID scope.</summary>
    public string? TagSetOid { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Entitlement attached to a directory group. Any subject that is a member of the group
/// inherits this grant (subject to user-level denials).
/// </summary>
public class GroupEntitlementEntity
{
    /// <summary>Surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Tenant identifier.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Group identifier — typically an Entra/Azure AD group object id, but may be any
    /// opaque identifier returned by the directory PIP source (LDAP DN, SAML group, etc.).
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>SPIF policy OID covered by this grant.</summary>
    public string PolicyOid { get; set; } = "";

    /// <summary>Specific classification LACV (null = whole policy).</summary>
    public int? ClassificationLacv { get; set; }

    /// <summary>Optional tag set OID scope.</summary>
    public string? TagSetOid { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Mode of a user entitlement override.
/// </summary>
public enum UserEntitlementOverrideMode
{
    /// <summary>Explicit user grant.</summary>
    Grant = 0,

    /// <summary>Explicit user deny — takes precedence over baseline and group grants.</summary>
    Deny = 1
}

/// <summary>
/// A user-level override. A Grant adds to the effective set on top of baseline + groups;
/// a Deny removes from it.
/// </summary>
public class UserEntitlementOverrideEntity
{
    /// <summary>Surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>Tenant identifier.</summary>
    public string? TenantId { get; set; }

    /// <summary>User subject identifier.</summary>
    public string UserId { get; set; } = "";

    /// <summary>SPIF policy OID covered.</summary>
    public string PolicyOid { get; set; } = "";

    /// <summary>Specific classification LACV (null = whole policy).</summary>
    public int? ClassificationLacv { get; set; }

    /// <summary>Optional tag set OID scope.</summary>
    public string? TagSetOid { get; set; }

    /// <summary>Whether this override is a Grant or a Deny.</summary>
    public UserEntitlementOverrideMode Mode { get; set; }

    /// <summary>Optional free-text reason recorded for audit purposes.</summary>
    public string? Reason { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

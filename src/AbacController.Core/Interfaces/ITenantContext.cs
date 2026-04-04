namespace AbacController.Core.Interfaces;

/// <summary>
/// Provides the current tenant context for multi-tenant isolation.
/// Tenant ID is resolved from request headers (X-Tenant-Id) or JWT claims (tenant_id).
/// A null TenantId indicates the system/default tenant.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current tenant identifier, or <c>null</c> for the system/default tenant.
    /// </summary>
    string? TenantId { get; }
}

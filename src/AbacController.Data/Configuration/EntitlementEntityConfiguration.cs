using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AbacController.Data.Configuration;

/// <summary>
/// Configures EF Core persistence for tenant baseline entitlements.
/// </summary>
public class TenantBaselineEntitlementEntityConfiguration : IEntityTypeConfiguration<TenantBaselineEntitlementEntity>
{
    /// <summary>Configures table, keys, and indexes.</summary>
    public void Configure(EntityTypeBuilder<TenantBaselineEntitlementEntity> builder)
    {
        builder.ToTable("TenantBaselineEntitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PolicyOid).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TagSetOid).HasMaxLength(256);
        builder.HasIndex(e => new { e.TenantId, e.PolicyOid });
        builder.HasIndex(e => new { e.TenantId, e.PolicyOid, e.ClassificationLacv }).IsUnique();
    }
}

/// <summary>
/// Configures EF Core persistence for directory-group entitlements.
/// </summary>
public class GroupEntitlementEntityConfiguration : IEntityTypeConfiguration<GroupEntitlementEntity>
{
    /// <summary>Configures table, keys, and indexes.</summary>
    public void Configure(EntityTypeBuilder<GroupEntitlementEntity> builder)
    {
        builder.ToTable("GroupEntitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.GroupId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PolicyOid).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TagSetOid).HasMaxLength(256);
        builder.HasIndex(e => new { e.TenantId, e.GroupId });
        builder.HasIndex(e => new { e.TenantId, e.GroupId, e.PolicyOid, e.ClassificationLacv }).IsUnique();
    }
}

/// <summary>
/// Configures EF Core persistence for per-user entitlement overrides.
/// </summary>
public class UserEntitlementOverrideEntityConfiguration : IEntityTypeConfiguration<UserEntitlementOverrideEntity>
{
    /// <summary>Configures table, keys, and indexes.</summary>
    public void Configure(EntityTypeBuilder<UserEntitlementOverrideEntity> builder)
    {
        builder.ToTable("UserEntitlementOverrides");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PolicyOid).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TagSetOid).HasMaxLength(256);
        builder.Property(e => e.Reason).HasMaxLength(512);
        builder.Property(e => e.Mode).HasConversion<int>();
        builder.HasIndex(e => new { e.TenantId, e.UserId });
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.PolicyOid, e.ClassificationLacv, e.Mode }).IsUnique();
    }
}

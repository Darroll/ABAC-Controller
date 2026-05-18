using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AbacController.Data.Configuration;

/// <summary>
/// Configures EF Core persistence for stored SPIF documents.
/// </summary>
public class SpifEntityConfiguration : IEntityTypeConfiguration<SpifEntity>
{
    /// <summary>
    /// Configures the <see cref="SpifEntity"/> table, keys, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<SpifEntity> builder)
    {
        builder.ToTable("Spifs");
        builder.HasKey(e => e.Id);

        // Soft-delete: every query sees only live rows by default. Writers
        // that need to re-import a previously deleted OID must use
        // `IgnoreQueryFilters()`.
        builder.HasQueryFilter(e => !e.IsDeleted);

        // Multi-tenancy: a (TenantId, PolicyOid) pair is globally unique but
        // the same PolicyOid MAY be registered independently in different
        // tenants. The uniqueness is FILTERED to live rows only so a
        // soft-deleted row does not block a fresh re-import of the same OID.
        builder.HasIndex(e => new { e.TenantId, e.PolicyOid })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = 0");

        // Secondary lookup for tenant-scoped listing hot paths.
        builder.HasIndex(e => e.TenantId);

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PolicyOid).HasMaxLength(256).IsRequired();
        builder.Property(e => e.SchemaVersion).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Hash).HasMaxLength(128);
    }
}

/// <summary>
/// Configures EF Core persistence for policy-set definitions.
/// </summary>
public class PolicySetEntityConfiguration : IEntityTypeConfiguration<PolicySetEntity>
{
    /// <summary>
    /// Configures the <see cref="PolicySetEntity"/> table, keys, relationships, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<PolicySetEntity> builder)
    {
        builder.ToTable("PolicySets");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.CombiningAlgorithm).HasMaxLength(64);
        builder.HasOne(e => e.Spif).WithMany().HasForeignKey(e => e.SpifId).IsRequired(false);
        builder.HasMany(e => e.Policies).WithOne(p => p.PolicySet).HasForeignKey(p => p.PolicySetId);
    }
}

/// <summary>
/// Configures EF Core persistence for policy definitions.
/// </summary>
public class PolicyEntityConfiguration : IEntityTypeConfiguration<PolicyEntity>
{
    /// <summary>
    /// Configures the <see cref="PolicyEntity"/> table, keys, relationships, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<PolicyEntity> builder)
    {
        builder.ToTable("Policies");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Format).HasMaxLength(32);
        builder.HasMany(e => e.Versions).WithOne(v => v.Policy).HasForeignKey(v => v.PolicyId);
    }
}

/// <summary>
/// Configures EF Core persistence for immutable policy versions.
/// </summary>
public class PolicyVersionEntityConfiguration : IEntityTypeConfiguration<PolicyVersionEntity>
{
    /// <summary>
    /// Configures the <see cref="PolicyVersionEntity"/> table, keys, indexes, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<PolicyVersionEntity> builder)
    {
        builder.ToTable("PolicyVersions");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PolicyId, e.VersionNumber }).IsUnique();
        builder.Property(e => e.Hash).HasMaxLength(128);
    }
}

/// <summary>
/// Configures EF Core persistence for PIP source definitions.
/// </summary>
public class PipSourceEntityConfiguration : IEntityTypeConfiguration<PipSourceEntity>
{
    /// <summary>
    /// Configures the <see cref="PipSourceEntity"/> table, keys, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<PipSourceEntity> builder)
    {
        builder.ToTable("PipSources");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(64).IsRequired();
    }
}

/// <summary>
/// Configures EF Core persistence for registered enforcement points.
/// </summary>
public class EnforcementPointEntityConfiguration : IEntityTypeConfiguration<EnforcementPointEntity>
{
    /// <summary>
    /// Configures the <see cref="EnforcementPointEntity"/> table, keys, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<EnforcementPointEntity> builder)
    {
        builder.ToTable("EnforcementPoints");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Type).HasMaxLength(64);
        builder.Property(e => e.EnforcementMode).HasMaxLength(32);
    }
}

/// <summary>
/// Configures EF Core persistence for audit events captured by the runtime.
/// </summary>
public class AuditEventEntityConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    /// <summary>
    /// Configures the <see cref="AuditEventEntity"/> table, indexes, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<AuditEventEntity> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.SubjectId);
        builder.HasIndex(e => e.ResourceId);
        builder.HasIndex(e => e.DecisionId);
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
    }
}

/// <summary>
/// Configures EF Core persistence for key/value configuration entries.
/// </summary>
public class ConfigurationEntryEntityConfiguration : IEntityTypeConfiguration<ConfigurationEntryEntity>
{
    /// <summary>
    /// Configures the <see cref="ConfigurationEntryEntity"/> table and key constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<ConfigurationEntryEntity> builder)
    {
        builder.ToTable("Configuration");
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasMaxLength(256);
    }
}

/// <summary>
/// Configures EF Core persistence for application registration entities.
/// </summary>
public class ApplicationRegistrationEntityConfiguration : IEntityTypeConfiguration<ApplicationRegistrationEntity>
{
    /// <summary>
    /// Configures the <see cref="ApplicationRegistrationEntity"/> table, keys, and column constraints.
    /// </summary>
    public void Configure(EntityTypeBuilder<ApplicationRegistrationEntity> builder)
    {
        builder.ToTable("ApplicationRegistrations");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(128);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.DefaultPolicyOid).HasMaxLength(256);
        builder.HasIndex(e => e.TenantId);
    }
}

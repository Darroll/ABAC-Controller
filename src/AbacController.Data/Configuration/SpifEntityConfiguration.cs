using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AbacController.Data.Configuration;

/// <summary>
/// EF Core entity configuration for <see cref="SpifEntity"/>.
/// </summary>
public class SpifEntityConfiguration : IEntityTypeConfiguration<SpifEntity>
{
    /// <summary>
    /// Executes configure.
    /// </summary>
    public void Configure(EntityTypeBuilder<SpifEntity> builder)
    {
        builder.ToTable("Spifs");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.PolicyOid).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PolicyOid).HasMaxLength(256).IsRequired();
        builder.Property(e => e.SchemaVersion).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Hash).HasMaxLength(128);
    }
}

/// <summary>
/// A PolicySetEntityConfiguration class.
/// </summary>
public class PolicySetEntityConfiguration : IEntityTypeConfiguration<PolicySetEntity>
{
    /// <summary>
    /// Executes configure.
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
/// A PolicyEntityConfiguration class.
/// </summary>
public class PolicyEntityConfiguration : IEntityTypeConfiguration<PolicyEntity>
{
    /// <summary>
    /// Executes configure.
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
/// A PolicyVersionEntityConfiguration class.
/// </summary>
public class PolicyVersionEntityConfiguration : IEntityTypeConfiguration<PolicyVersionEntity>
{
    /// <summary>
    /// Executes configure.
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
/// A PipSourceEntityConfiguration class.
/// </summary>
public class PipSourceEntityConfiguration : IEntityTypeConfiguration<PipSourceEntity>
{
    /// <summary>
    /// Executes configure.
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
/// An EnforcementPointEntityConfiguration class.
/// </summary>
public class EnforcementPointEntityConfiguration : IEntityTypeConfiguration<EnforcementPointEntity>
{
    /// <summary>
    /// Executes configure.
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
/// An AuditEventEntityConfiguration class.
/// </summary>
public class AuditEventEntityConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    /// <summary>
    /// Executes configure.
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
/// A ConfigurationEntryEntityConfiguration class.
/// </summary>
public class ConfigurationEntryEntityConfiguration : IEntityTypeConfiguration<ConfigurationEntryEntity>
{
    /// <summary>
    /// Executes configure.
    /// </summary>
    public void Configure(EntityTypeBuilder<ConfigurationEntryEntity> builder)
    {
        builder.ToTable("Configuration");
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasMaxLength(256);
    }
}

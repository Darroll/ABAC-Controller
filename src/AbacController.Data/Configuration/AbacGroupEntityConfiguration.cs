using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AbacController.Data.Configuration;

/// <summary>Configures EF Core persistence for ABAC group definitions.</summary>
public class AbacGroupEntityConfiguration : IEntityTypeConfiguration<AbacGroupEntity>
{
    /// <summary>Configures table, keys, and indexes.</summary>
    public void Configure(EntityTypeBuilder<AbacGroupEntity> builder)
    {
        builder.ToTable("AbacGroups");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1024);
        builder.Property(e => e.TenantId).HasMaxLength(128);

        // Group names are unique within a tenant. The PK already covers Id;
        // this index lets the admin UI find a group by name and prevents
        // accidental duplicates.
        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
    }
}

/// <summary>Configures EF Core persistence for ABAC group membership rows.</summary>
public class AbacGroupMembershipEntityConfiguration : IEntityTypeConfiguration<AbacGroupMembershipEntity>
{
    /// <summary>Configures composite key, columns, and the resolver covering index.</summary>
    public void Configure(EntityTypeBuilder<AbacGroupMembershipEntity> builder)
    {
        builder.ToTable("AbacGroupMemberships");
        builder.HasKey(e => new { e.AbacGroupId, e.Kind, e.MemberId });
        builder.Property(e => e.MemberId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.TenantId).HasMaxLength(128);

        // Hot path: ResolveAbacGroupIdsAsync filters by
        // (TenantId, Kind, MemberId) and selects AbacGroupId. SQLite doesn't
        // support INCLUDE columns but a composite index on the filter columns
        // still makes the lookup an index seek.
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.MemberId });

        // Listing all members of a group is also a common query.
        builder.HasIndex(e => e.AbacGroupId);
    }
}

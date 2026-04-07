using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data;

/// <summary>
/// EF Core DbContext for the ABAC Controller.
/// </summary>
public class AbacDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="AbacDbContext"/> class.</summary>
    public AbacDbContext(DbContextOptions<AbacDbContext> options) : base(options) { }

    /// <summary>Stored SPIF records.</summary>
    public DbSet<SpifEntity> Spifs => Set<SpifEntity>();

    /// <summary>Stored policy sets.</summary>
    public DbSet<PolicySetEntity> PolicySets => Set<PolicySetEntity>();

    /// <summary>Stored policies.</summary>
    public DbSet<PolicyEntity> Policies => Set<PolicyEntity>();

    /// <summary>Stored policy versions.</summary>
    public DbSet<PolicyVersionEntity> PolicyVersions => Set<PolicyVersionEntity>();

    /// <summary>Configured PIP sources.</summary>
    public DbSet<PipSourceEntity> PipSources => Set<PipSourceEntity>();

    /// <summary>Registered enforcement points.</summary>
    public DbSet<EnforcementPointEntity> EnforcementPoints => Set<EnforcementPointEntity>();

    /// <summary>Persisted audit events.</summary>
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    /// <summary>Configuration entries.</summary>
    public DbSet<ConfigurationEntryEntity> Configuration => Set<ConfigurationEntryEntity>();

    /// <summary>Registered application classification scopes.</summary>
    public DbSet<ApplicationRegistrationEntity> ApplicationRegistrations => Set<ApplicationRegistrationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AbacDbContext).Assembly);
    }

    /// <summary>
    /// Initialize SQLite-specific PRAGMAs for performance.
    /// </summary>
    public void InitializeSqlite()
    {
        if (Database.IsSqlite())
        {
            Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
            Database.ExecuteSqlRaw("PRAGMA mmap_size=268435456;");
            Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
        }
    }
}

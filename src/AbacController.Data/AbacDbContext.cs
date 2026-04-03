using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data;

/// <summary>
/// EF Core DbContext for the ABAC Controller.
/// </summary>
public class AbacDbContext : DbContext
{
    public AbacDbContext(DbContextOptions<AbacDbContext> options) : base(options) { }

    public DbSet<SpifEntity> Spifs => Set<SpifEntity>();
    public DbSet<PolicySetEntity> PolicySets => Set<PolicySetEntity>();
    public DbSet<PolicyEntity> Policies => Set<PolicyEntity>();
    public DbSet<PolicyVersionEntity> PolicyVersions => Set<PolicyVersionEntity>();
    public DbSet<PipSourceEntity> PipSources => Set<PipSourceEntity>();
    public DbSet<EnforcementPointEntity> EnforcementPoints => Set<EnforcementPointEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<ConfigurationEntryEntity> Configuration => Set<ConfigurationEntryEntity>();

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

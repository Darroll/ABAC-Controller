namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for a PIP attribute source configuration.
/// </summary>
public class PipSourceEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public string ProvidesAttributes { get; set; } = "";
    public int Priority { get; set; }
    public bool IsRequired { get; set; }
    public bool CacheEnabled { get; set; } = true;
    public int CacheTtlSeconds { get; set; } = 300;
    public int CacheMaxEntries { get; set; } = 1000;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for key-value configuration entries.
/// </summary>
public class ConfigurationEntryEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}

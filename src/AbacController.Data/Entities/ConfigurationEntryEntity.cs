namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for key-value configuration entries.
/// </summary>
public class ConfigurationEntryEntity
{
    /// <summary>Gets or sets the key.</summary>
    public string Key { get; set; } = "";
    /// <summary>Gets or sets the value.</summary>
    public string Value { get; set; } = "";
    /// <summary>Gets or sets the updated At.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Gets or sets the updated By.</summary>
    public string? UpdatedBy { get; set; }
}

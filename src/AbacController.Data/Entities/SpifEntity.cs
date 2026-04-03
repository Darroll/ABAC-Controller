namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for SPIF storage.
/// </summary>
public class SpifEntity
{
    public Guid Id { get; set; }
    public string PolicyOid { get; set; } = "";
    public string Name { get; set; } = "";
    public string SchemaVersion { get; set; } = "2.1";
    public string RawXml { get; set; } = "";
    public string? CreationDate { get; set; }
    public string? Version { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ImportedBy { get; set; }
    public int ClassificationCount { get; set; }
    public int CategoryCount { get; set; }
    public SignatureStatus SignatureStatus { get; set; }
    public string Hash { get; set; } = "";
}

public enum SignatureStatus
{
    NotSigned,
    Valid,
    Invalid
}

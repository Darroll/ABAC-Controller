namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for SPIF storage.
/// </summary>
public class SpifEntity
{
    /// <summary>Unique SPIF entity identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Security policy OID (globally unique).</summary>
    public string PolicyOid { get; set; } = "";

    /// <summary>Human-readable policy name.</summary>
    public string Name { get; set; } = "";

    /// <summary>SPIF schema version ("2.1" or "3.0").</summary>
    public string SchemaVersion { get; set; } = "2.1";

    /// <summary>Raw XML content of the SPIF document.</summary>
    public string RawXml { get; set; } = "";

    /// <summary>SPIF creation date from the document.</summary>
    public string? CreationDate { get; set; }

    /// <summary>SPIF version string from the document.</summary>
    public string? Version { get; set; }

    /// <summary>Whether this SPIF is the active version for its policy OID.</summary>
    public bool IsActive { get; set; }

    /// <summary>When this SPIF was imported into the system (UTC).</summary>
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Identity of the user who imported this SPIF.</summary>
    public string? ImportedBy { get; set; }

    /// <summary>Number of security classifications defined.</summary>
    public int ClassificationCount { get; set; }

    /// <summary>Total number of security categories across all tag sets.</summary>
    public int CategoryCount { get; set; }

    /// <summary>XML digital signature verification status.</summary>
    public SignatureStatus SignatureStatus { get; set; }

    /// <summary>SHA256 hash of the raw XML for change detection.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Tenant identifier for multi-tenant isolation. Null = default/system tenant.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Soft-delete marker. Rows with <c>IsDeleted=true</c> are invisible to
    /// standard queries via the global EF query filter but remain in the
    /// table for audit continuity. Re-importing a previously soft-deleted
    /// policy OID must bypass the query filter (see <c>PapAdminController</c>
    /// and <c>BundledSpifImporter</c>).
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>Timestamp of the soft-delete, or null for live rows.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// XML digital signature verification status for SPIF documents.
/// </summary>
public enum SignatureStatus
{
    /// <summary>SPIF has no XML-DSig signature.</summary>
    NotSigned,

    /// <summary>XML-DSig signature verified successfully.</summary>
    Valid,

    /// <summary>XML-DSig signature verification failed.</summary>
    Invalid
}

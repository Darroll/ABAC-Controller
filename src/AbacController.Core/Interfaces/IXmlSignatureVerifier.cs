using System.Xml.Linq;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Pluggable XML-DSig verification interface for SPIF validation.
/// </summary>
public interface IXmlSignatureVerifier
{
    /// <summary>
    /// Verify any XML signature/trust hint associated with the supplied document.
    /// Implementations may reject signed content when verification is unavailable.
    /// </summary>
    SignatureVerificationResult Verify(XDocument document, string? keyIdentifier = null);
}

/// <summary>
/// Result of XML signature verification.
/// </summary>
public sealed record SignatureVerificationResult
{
    /// <summary>Whether verification succeeded or was not required.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Whether a verification attempt was actually made.</summary>
    public required bool WasAttempted { get; init; }

    /// <summary>Human-readable message describing the result.</summary>
    public string? Message { get; init; }

    /// <summary>Creates a successful verification result.</summary>
    public static SignatureVerificationResult Valid(string? message = null) => new()
    {
        IsSuccess = true,
        WasAttempted = true,
        Message = message
    };

    /// <summary>Creates a result indicating verification was skipped (no signature present).</summary>
    public static SignatureVerificationResult Skipped(string? message = null) => new()
    {
        IsSuccess = true,
        WasAttempted = false,
        Message = message
    };

    /// <summary>Creates a failed verification result.</summary>
    public static SignatureVerificationResult Invalid(string message) => new()
    {
        IsSuccess = false,
        WasAttempted = true,
        Message = message
    };
}

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
    public required bool IsSuccess { get; init; }
    public required bool WasAttempted { get; init; }
    public string? Message { get; init; }

    public static SignatureVerificationResult Valid(string? message = null) => new()
    {
        IsSuccess = true,
        WasAttempted = true,
        Message = message
    };

    public static SignatureVerificationResult Skipped(string? message = null) => new()
    {
        IsSuccess = true,
        WasAttempted = false,
        Message = message
    };

    public static SignatureVerificationResult Invalid(string message) => new()
    {
        IsSuccess = false,
        WasAttempted = true,
        Message = message
    };
}

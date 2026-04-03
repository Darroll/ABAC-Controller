using System.Xml.Linq;
using AbacController.Core.Interfaces;

namespace AbacController.Pap;

/// <summary>
/// Default safe verifier: unsigned SPIFs pass through, but signed SPIFs are rejected
/// unless a real verifier is substituted.
/// </summary>
public sealed class RejectingXmlSignatureVerifier : IXmlSignatureVerifier
{
    public SignatureVerificationResult Verify(XDocument document, string? keyIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var hasSignature = document
            .Descendants()
            .Any(static e => string.Equals(e.Name.LocalName, "Signature", StringComparison.Ordinal));

        if (!hasSignature && string.IsNullOrWhiteSpace(keyIdentifier))
        {
            return SignatureVerificationResult.Skipped("No XML-DSig signature or keyIdentifier present.");
        }

        return SignatureVerificationResult.Invalid(
            "XML-DSig verification is not configured. Signed SPIF input is rejected until a verifier implementation is provided.");
    }
}

/// <summary>
/// Optional permissive verifier for explicit local use in tests or controlled scenarios.
/// </summary>
public sealed class NoOpXmlSignatureVerifier : IXmlSignatureVerifier
{
    public SignatureVerificationResult Verify(XDocument document, string? keyIdentifier = null)
        => SignatureVerificationResult.Skipped(
            "XML-DSig verification bypassed by NoOpXmlSignatureVerifier.");
}

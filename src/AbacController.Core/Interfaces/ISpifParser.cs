using AbacController.Core.Domain.Labels;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Parses XML SPIF documents into domain model objects.
/// Supports v2.1 and v3.0 schemas.
/// </summary>
public interface ISpifParser
{
    /// <summary>
    /// Parse an XML SPIF document string into a SPIF domain object.
    /// Performs namespace normalization (xmslpif→xmlspif typo),
    /// schema validation, and semantic validation.
    /// </summary>
    SpifParseResult Parse(string xmlContent);

    /// <summary>Parse from a stream (for large SPIFs).</summary>
    SpifParseResult Parse(Stream xmlStream);

    /// <summary>Validate an XML string against the SPIF XSD without full parsing.</summary>
    ValidationResult ValidateSchema(string xmlContent);
}

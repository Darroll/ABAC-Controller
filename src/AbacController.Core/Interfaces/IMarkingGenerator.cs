using AbacController.Core.Domain.Labels;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Generates human-readable marking strings from security labels using SPIF rules.
/// </summary>
public interface IMarkingGenerator
{
    /// <summary>
    /// Generate a marking string for the given label using the SPIF's marking data
    /// and qualifiers (e.g., "NATO SECRET//REL TO USA, GBR").
    /// </summary>
    string GenerateMarking(SecurityLabel label, ISpifIndex spifIndex, string markingCode = "pageTop");

    /// <summary>
    /// Generate a short marking (abbreviated) if available.
    /// </summary>
    string GenerateShortMarking(SecurityLabel label, ISpifIndex spifIndex);
}

using System.Collections.Immutable;

namespace AbacController.Core.Domain.Spif;

/// <summary>
/// Marking instructions for rendering a security element in human-readable form.
/// </summary>
public sealed record MarkingData
{
    /// <summary>Default marking phrase (e.g., "NATO SECRET").</summary>
    public required string Phrase { get; init; }

    /// <summary>Abbreviated phrase for compact display (e.g., "NS").</summary>
    public string? ShortPhrase { get; init; }

    /// <summary>Restricted charset phrase for protocol headers.</summary>
    public string? SimplePhrase { get; init; }

    /// <summary>Used only during user input, not in output.</summary>
    public string? InputPhrase { get; init; }

    /// <summary>Language tag (e.g., "en", "fr").</summary>
    public string? Language { get; init; }

    /// <summary>Display location codes (e.g., "pageTop", "pageBottom").</summary>
    public ImmutableList<string> Codes { get; init; } = ImmutableList<string>.Empty;
}

/// <summary>
/// Marking qualifier — adds prefix/suffix/separator to marking phrases.
/// </summary>
public sealed record MarkingQualifier
{
    /// <summary>The marking code this qualifier applies to (e.g., "pageTop").</summary>
    public required string MarkingCode { get; init; }

    /// <summary>Qualifier entries (prefix, suffix, separator).</summary>
    public ImmutableList<QualifierEntry> Qualifiers { get; init; } = ImmutableList<QualifierEntry>.Empty;
}

/// <summary>
/// A single qualifier entry (prefix, suffix, separator, finalSeparator).
/// </summary>
public sealed record QualifierEntry
{
    /// <summary>The qualifier text (e.g., "REL TO ", "/").</summary>
    public required string Text { get; init; }

    /// <summary>Qualifier type code.</summary>
    public required QualifierCode Code { get; init; }
}

/// <summary>
/// Qualifier code types.
/// </summary>
public enum QualifierCode
{
    Prefix,
    Suffix,
    Separator,
    FinalSeparator
}

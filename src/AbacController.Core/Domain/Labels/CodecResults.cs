namespace AbacController.Core.Domain.Labels;

/// <summary>
/// Result of encoding a security label into a wire format.
/// </summary>
public sealed record EncodeResult
{
    /// <summary>Whether encoding succeeded.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Encoded label as string (for text-based codecs like XML).</summary>
    public string? EncodedString { get; init; }

    /// <summary>Encoded label as bytes (for binary codecs like BER/DER).</summary>
    public byte[]? EncodedBytes { get; init; }

    /// <summary>Error message if encoding failed.</summary>
    public string? Error { get; init; }

    /// <summary>Create a successful string result.</summary>
    public static EncodeResult Success(string encoded) => new()
    {
        IsSuccess = true,
        EncodedString = encoded
    };

    /// <summary>Create a successful bytes result.</summary>
    public static EncodeResult Success(byte[] encoded) => new()
    {
        IsSuccess = true,
        EncodedBytes = encoded
    };

    /// <summary>Create a failed result.</summary>
    public static EncodeResult Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };
}

/// <summary>
/// Result of decoding a wire-format label into a domain object.
/// </summary>
public sealed record DecodeResult
{
    /// <summary>Whether decoding succeeded.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Decoded security label.</summary>
    public SecurityLabel? Label { get; init; }

    /// <summary>Error message if decoding failed.</summary>
    public string? Error { get; init; }

    /// <summary>Warnings during decode (non-fatal issues).</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>Create a successful result.</summary>
    public static DecodeResult Success(SecurityLabel label) => new()
    {
        IsSuccess = true,
        Label = label
    };

    /// <summary>Create a failed result.</summary>
    public static DecodeResult Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };
}

/// <summary>
/// Result of SPIF parsing.
/// </summary>
public sealed record SpifParseResult
{
    /// <summary>Whether parsing succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Parsed SPIF domain object.</summary>
    public Core.Domain.Spif.Spif? Spif { get; init; }

    /// <summary>Warnings (non-fatal).</summary>
    public IReadOnlyList<SpifParseWarning> Warnings { get; init; } = [];

    /// <summary>Errors (fatal).</summary>
    public IReadOnlyList<SpifParseError> Errors { get; init; } = [];

    /// <summary>Create a successful result.</summary>
    public static SpifParseResult Succeeded(Core.Domain.Spif.Spif spif,
        List<SpifParseWarning>? warnings = null) => new()
    {
        Success = true,
        Spif = spif,
        Warnings = warnings ?? []
    };

    /// <summary>Create a failed result.</summary>
    public static SpifParseResult Failed(IReadOnlyList<SpifParseError> errors,
        IReadOnlyList<SpifParseWarning>? warnings = null) => new()
    {
        Success = false,
        Errors = errors,
        Warnings = warnings ?? []
    };
}

/// <summary>
/// A warning during SPIF parsing.
/// </summary>
public sealed record SpifParseWarning(string Message, int? LineNumber = null);

/// <summary>
/// An error during SPIF parsing.
/// </summary>
public sealed record SpifParseError(string Message, int? LineNumber = null);

/// <summary>
/// Result of schema validation.
/// </summary>
public sealed record ValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<SpifParseError> Errors { get; init; } = [];

    public static ValidationResult Valid() => new() { IsValid = true };
    public static ValidationResult Invalid(IReadOnlyList<SpifParseError> errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

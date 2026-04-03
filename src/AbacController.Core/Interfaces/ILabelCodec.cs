using AbacController.Core.Domain.Labels;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Pluggable label codec for encoding/decoding security labels
/// in different wire formats. v1.0 ships with XML/STANAG 4774.
/// </summary>
public interface ILabelCodec
{
    /// <summary>Unique identifier for this codec (e.g., "stanag4774-xml").</summary>
    string CodecId { get; }

    /// <summary>MIME type for this codec's encoded format.</summary>
    string ContentType { get; }

    /// <summary>Encode a SecurityLabel domain object into the codec's wire format.</summary>
    EncodeResult Encode(SecurityLabel label, ISpifIndex spifIndex);

    /// <summary>Decode a wire-format label into a SecurityLabel domain object.</summary>
    DecodeResult Decode(ReadOnlySpan<byte> encodedLabel);

    /// <summary>Decode from string representation (for XML-based codecs).</summary>
    DecodeResult Decode(string encodedLabel);
}

/// <summary>
/// Registry for label codecs. Manages codec registration and lookup.
/// </summary>
public interface ILabelCodecRegistry
{
    /// <summary>Register a codec. Called during DI setup.</summary>
    void Register(ILabelCodec codec);

    /// <summary>Get codec by ID.</summary>
    ILabelCodec GetCodec(string codecId);

    /// <summary>Get codec by content type.</summary>
    ILabelCodec GetCodecByContentType(string contentType);

    /// <summary>List all registered codec IDs.</summary>
    IReadOnlyList<string> GetRegisteredCodecIds();
}

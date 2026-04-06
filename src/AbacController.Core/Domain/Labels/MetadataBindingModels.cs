using System.Text;

namespace AbacController.Core.Domain.Labels;

/// <summary>
/// Minimal STANAG 4778-style metadata binding envelope used by the controller.
/// This binds an encoded STANAG 4774 label together with opaque payload content.
/// </summary>
public sealed record MetadataBindingEnvelope
{
    /// <summary>Optional caller-supplied binding identifier.</summary>
    public string? BindingId { get; init; }

    /// <summary>Creation timestamp for the envelope.</summary>
    /// <remarks>Not persisted in the STANAG 4778 BDO wire format. On unbind, this
    /// field returns <see cref="DateTimeOffset.UtcNow"/>. Timestamps belong in the
    /// STANAG 4774 label's CreationDateTime element, not in the BDO envelope.</remarks>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Encoded STANAG 4774 label XML carried in the envelope.</summary>
    public required string LabelXml { get; init; }

    /// <summary>Opaque payload bytes carried inline in the envelope.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>Optional media type for the protected payload.</summary>
    public string? MediaType { get; init; }

    /// <summary>Convenience accessor for textual payloads.</summary>
    public string GetPayloadText() => Encoding.UTF8.GetString(Payload);
}

/// <summary>
/// Result of extracting a metadata binding envelope.
/// </summary>
public sealed record MetadataUnbindResult
{
    /// <summary>Parsed envelope values.</summary>
    public required MetadataBindingEnvelope Envelope { get; init; }

    /// <summary>Decoded security label, when decoding succeeds.</summary>
    public SecurityLabel? Label { get; init; }
}

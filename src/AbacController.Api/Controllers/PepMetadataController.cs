using System.Text;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// Minimal PEP metadata binding endpoints for wrapping/unwrapping STANAG 4774
/// labels in a STANAG 4778-style envelope.
/// </summary>
[ApiController]
[Route("api/v1/pep/metadata")]
public sealed class PepMetadataController : ControllerBase
{
    private readonly IStanag4778MetadataBinder _binder;
    private readonly ILabelCodecRegistry _codecs;

    /// <summary>Initializes a new instance of the <see cref="PepMetadataController"/> class.</summary>
    public PepMetadataController(IStanag4778MetadataBinder binder, ILabelCodecRegistry codecs)
    {
        _binder = binder;
        _codecs = codecs;
    }

    /// <summary>Bind a STANAG 4774 label and payload into a STANAG 4778 metadata envelope.</summary>
    [HttpPost("bind")]
    [Authorize(Policy = "PepLabel")]
    public ActionResult<MetadataEnvelopeResponse> Bind([FromBody] BindMetadataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LabelXml))
        {
            return BadRequest("labelXml is required.");
        }

        var payload = request.PayloadBase64 is { Length: > 0 }
            ? Convert.FromBase64String(request.PayloadBase64)
            : Encoding.UTF8.GetBytes(request.PayloadText ?? string.Empty);

        var envelopeXml = _binder.Bind(new MetadataBindingEnvelope
        {
            BindingId = request.BindingId,
            CreatedAt = request.CreatedAt ?? DateTimeOffset.UtcNow,
            LabelXml = request.LabelXml,
            Payload = payload,
            MediaType = request.MediaType
        });

        return Ok(new MetadataEnvelopeResponse { EnvelopeXml = envelopeXml });
    }

    /// <summary>Unbind a STANAG 4778 metadata envelope into its label and payload components.</summary>
    [HttpPost("unbind")]
    [Authorize(Policy = "PepLabel")]
    public ActionResult<UnboundMetadataResponse> Unbind([FromBody] UnbindMetadataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EnvelopeXml))
        {
            return BadRequest("envelopeXml is required.");
        }

        var result = _binder.Unbind(request.EnvelopeXml);

        return Ok(new UnboundMetadataResponse
        {
            BindingId = result.Envelope.BindingId,
            CreatedAt = result.Envelope.CreatedAt,
            LabelXml = result.Envelope.LabelXml,
            MediaType = result.Envelope.MediaType,
            PayloadBase64 = Convert.ToBase64String(result.Envelope.Payload),
            PayloadText = TryDecodeUtf8(result.Envelope.Payload),
            Label = result.Label
        });
    }

    /// <summary>List registered label codec identifiers.</summary>
    [HttpGet("codecs")]
    [Authorize(Policy = "PepRead")]
    public ActionResult<IReadOnlyList<string>> GetCodecs()
        => Ok(_codecs.GetRegisteredCodecIds());

    /// <summary>Attempts to decode a byte array as UTF-8 text.</summary>
    private static string? TryDecodeUtf8(byte[] payload)
    {
        try
        {
            return Encoding.UTF8.GetString(payload);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Request to bind a label and payload into a metadata envelope.</summary>
public sealed class BindMetadataRequest
{
    /// <summary>Optional binding identifier.</summary>
    public string? BindingId { get; set; }

    /// <summary>Timestamp of the binding. Defaults to now if not specified.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>STANAG 4774 label XML.</summary>
    public string? LabelXml { get; set; }

    /// <summary>MIME type of the payload.</summary>
    public string? MediaType { get; set; }

    /// <summary>Payload as plain text (mutually exclusive with PayloadBase64).</summary>
    public string? PayloadText { get; set; }

    /// <summary>Payload as base64-encoded bytes.</summary>
    public string? PayloadBase64 { get; set; }
}

/// <summary>Request to unbind a metadata envelope.</summary>
public sealed class UnbindMetadataRequest
{
    /// <summary>STANAG 4778 metadata envelope XML to unbind.</summary>
    public string? EnvelopeXml { get; set; }
}

/// <summary>Response containing a bound metadata envelope.</summary>
public sealed class MetadataEnvelopeResponse
{
    /// <summary>The STANAG 4778 metadata envelope XML.</summary>
    public required string EnvelopeXml { get; init; }
}

/// <summary>Response containing the unbound components of a metadata envelope.</summary>
public sealed class UnboundMetadataResponse
{
    /// <summary>Binding identifier from the envelope.</summary>
    public string? BindingId { get; init; }

    /// <summary>When the binding was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>STANAG 4774 label XML extracted from the envelope.</summary>
    public required string LabelXml { get; init; }

    /// <summary>MIME type of the payload.</summary>
    public string? MediaType { get; init; }

    /// <summary>Payload as base64-encoded bytes.</summary>
    public required string PayloadBase64 { get; init; }

    /// <summary>Payload decoded as UTF-8 text, if possible.</summary>
    public string? PayloadText { get; init; }

    /// <summary>Parsed security label, if decoding succeeded.</summary>
    public SecurityLabel? Label { get; init; }
}

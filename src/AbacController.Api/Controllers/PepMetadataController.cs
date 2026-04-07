using System.Text;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;
using AbacController.Pep;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// Exposes PEP metadata binding endpoints for wrapping and unwrapping STANAG 4774 labels in a STANAG 4778-style envelope.
/// </summary>
[ApiController]
[Route("pep/api/metadata")]
public sealed class PepMetadataController : ControllerBase
{
    private readonly IStanag4778MetadataBinder _binder;
    private readonly ILabelCodecRegistry _codecs;
    private readonly BindingDataHeaderCodec _headerCodec;

    /// <summary>Initializes a new instance of the <see cref="PepMetadataController"/> class.</summary>
    public PepMetadataController(
        IStanag4778MetadataBinder binder,
        ILabelCodecRegistry codecs,
        BindingDataHeaderCodec headerCodec)
    {
        _binder = binder;
        _codecs = codecs;
        _headerCodec = headerCodec;
    }

    /// <summary>Builds a metadata envelope from label and payload inputs.</summary>
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

    /// <summary>Extracts label and payload data from a metadata envelope.</summary>
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

    /// <summary>
    /// Produces a STANAG 4778 <c>Binding-Data</c> HTTP header value for the HTTP entity body
    /// binding profile (ADatP-4778.2 Chapter 7). The label is bound to the HTTP entity body
    /// via a null-URI DataReference; the caller attaches the returned header to the HTTP request
    /// or response that carries the protected content.
    /// </summary>
    [HttpPost("bind-http")]
    [Authorize(Policy = "PepLabel")]
    public ActionResult<BindHttpMetadataResponse> BindHttp([FromBody] BindHttpMetadataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LabelXml))
        {
            return BadRequest("labelXml is required.");
        }

        var bdoXml = _binder.BindDetached(
            request.LabelXml,
            dataUri: "",
            contentType: "message/http",
            bindingId: request.BindingId);

        var headerValue = _headerCodec.Encode(bdoXml);

        return Ok(new BindHttpMetadataResponse
        {
            BindingDataHeaderValue = headerValue,
            BdoXml = bdoXml
        });
    }

    /// <summary>
    /// Extracts the security label from a STANAG 4778 <c>Binding-Data</c> HTTP header value
    /// (ADatP-4778.2 Chapter 7). No payload is returned — the protected data is the HTTP entity body.
    /// </summary>
    [HttpPost("unbind-http")]
    [Authorize(Policy = "PepLabel")]
    public ActionResult<UnbindHttpMetadataResponse> UnbindHttp([FromBody] UnbindHttpMetadataRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BindingDataHeaderValue))
        {
            return BadRequest("bindingDataHeaderValue is required.");
        }

        try
        {
            var bdoXml = _headerCodec.Decode(request.BindingDataHeaderValue);
            var result = _binder.Unbind(bdoXml);

            return Ok(new UnbindHttpMetadataResponse
            {
                BindingId = result.Envelope.BindingId,
                LabelXml = result.Envelope.LabelXml,
                Label = result.Label
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Lists the registered label codec identifiers available to the PEP surface.</summary>
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

/// <summary>Request to produce a Binding-Data header for the HTTP entity body binding profile.</summary>
public sealed class BindHttpMetadataRequest
{
    /// <summary>Optional binding identifier (valid XML NCName).</summary>
    public string? BindingId { get; set; }

    /// <summary>STANAG 4774 label XML to bind.</summary>
    public string? LabelXml { get; set; }
}

/// <summary>Response containing a Binding-Data header value for the HTTP entity body binding.</summary>
public sealed class BindHttpMetadataResponse
{
    /// <summary>
    /// The complete <c>Binding-Data</c> header value.
    /// Attach to the HTTP request or response that carries the protected content as:
    /// <c>Binding-Data: {BindingDataHeaderValue}</c>
    /// </summary>
    public required string BindingDataHeaderValue { get; init; }

    /// <summary>The raw STANAG 4778 BDO XML, for inspection or storage.</summary>
    public required string BdoXml { get; init; }
}

/// <summary>Request to extract a label from a Binding-Data HTTP header value.</summary>
public sealed class UnbindHttpMetadataRequest
{
    /// <summary>
    /// The <c>Binding-Data</c> header field value (everything after the colon),
    /// e.g. <c>binding-type="..."; binding-data-object="..."</c>.
    /// </summary>
    public string? BindingDataHeaderValue { get; set; }
}

/// <summary>
/// Response containing the security label extracted from a Binding-Data HTTP header.
/// MediaType and payload are omitted — the protected data is the HTTP entity body.
/// </summary>
public sealed class UnbindHttpMetadataResponse
{
    /// <summary>Binding identifier from the BDO, if present.</summary>
    public string? BindingId { get; init; }

    /// <summary>STANAG 4774 label XML extracted from the BDO.</summary>
    public required string LabelXml { get; init; }

    /// <summary>Parsed security label, if decoding succeeded.</summary>
    public SecurityLabel? Label { get; init; }
}

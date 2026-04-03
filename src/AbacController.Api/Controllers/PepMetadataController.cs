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

    public PepMetadataController(IStanag4778MetadataBinder binder, ILabelCodecRegistry codecs)
    {
        _binder = binder;
        _codecs = codecs;
    }

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

    [HttpGet("codecs")]
    [Authorize(Policy = "PepRead")]
    public ActionResult<IReadOnlyList<string>> GetCodecs()
        => Ok(_codecs.GetRegisteredCodecIds());

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

public sealed class BindMetadataRequest
{
    public string? BindingId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? LabelXml { get; set; }
    public string? MediaType { get; set; }
    public string? PayloadText { get; set; }
    public string? PayloadBase64 { get; set; }
}

public sealed class UnbindMetadataRequest
{
    public string? EnvelopeXml { get; set; }
}

public sealed class MetadataEnvelopeResponse
{
    public required string EnvelopeXml { get; init; }
}

public sealed class UnboundMetadataResponse
{
    public string? BindingId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required string LabelXml { get; init; }
    public string? MediaType { get; init; }
    public required string PayloadBase64 { get; init; }
    public string? PayloadText { get; init; }
    public SecurityLabel? Label { get; init; }
}

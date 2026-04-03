using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;

namespace AbacController.Pep;

/// <summary>
/// Minimal XML metadata binding service for carrying a STANAG 4774 label and
/// opaque payload content in a STANAG 4778-style envelope.
/// </summary>
public sealed class Stanag4778MetadataBinder : IStanag4778MetadataBinder
{
    private static readonly XNamespace BindingNs = SpifNamespaces.Stanag4778;

    private readonly ILabelCodec _labelCodec;

    public Stanag4778MetadataBinder(IEnumerable<ILabelCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        _labelCodec = codecs.FirstOrDefault(static codec =>
                string.Equals(codec.CodecId, "stanag4774-xml", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("STANAG 4774 XML label codec is required for metadata binding.");
    }

    public string Bind(MetadataBindingEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.LabelXml);
        ArgumentNullException.ThrowIfNull(envelope.Payload);

        var labelDocument = XDocument.Parse(envelope.LabelXml, LoadOptions.PreserveWhitespace);
        var labelRoot = labelDocument.Root ?? throw new InvalidOperationException("Label XML is missing a root element.");

        var document = new XDocument(
            new XElement(BindingNs + "MetadataBindingEnvelope",
                new XAttribute(XNamespace.Xmlns + "mbm", BindingNs),
                new XElement(BindingNs + "BindingIdentifier", envelope.BindingId ?? Guid.NewGuid().ToString("N")),
                new XElement(BindingNs + "BindingTime", envelope.CreatedAt.ToString("o")),
                new XElement(BindingNs + "Metadata", new XElement(labelRoot)),
                new XElement(BindingNs + "ProtectedObject",
                    string.IsNullOrWhiteSpace(envelope.MediaType)
                        ? null
                        : new XAttribute("mediaType", envelope.MediaType),
                    new XAttribute("encoding", "base64"),
                    Convert.ToBase64String(envelope.Payload))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public MetadataUnbindResult Unbind(string envelopeXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeXml);

        var document = XDocument.Parse(envelopeXml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("Envelope XML is missing a root element.");

        var bindingId = root.Elements().FirstOrDefault(static e => e.Name.LocalName == "BindingIdentifier")?.Value?.Trim();
        var bindingTimeValue = root.Elements().FirstOrDefault(static e => e.Name.LocalName == "BindingTime")?.Value?.Trim();
        var metadataElement = root.Elements().FirstOrDefault(static e => e.Name.LocalName == "Metadata");
        var labelElement = metadataElement?.Elements().FirstOrDefault();
        var protectedObject = root.Elements().FirstOrDefault(static e => e.Name.LocalName == "ProtectedObject");

        if (labelElement is null)
        {
            throw new InvalidOperationException("Envelope does not contain an embedded security label.");
        }

        if (protectedObject is null)
        {
            throw new InvalidOperationException("Envelope does not contain a protected object payload.");
        }

        var labelXml = labelElement.ToString(SaveOptions.DisableFormatting);
        var decodeResult = _labelCodec.Decode(labelXml);
        var payloadBase64 = protectedObject.Value?.Trim() ?? string.Empty;
        var payload = string.IsNullOrEmpty(payloadBase64) ? [] : Convert.FromBase64String(payloadBase64);

        var createdAt = DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(bindingTimeValue, out var parsedCreatedAt))
        {
            createdAt = parsedCreatedAt;
        }

        return new MetadataUnbindResult
        {
            Envelope = new MetadataBindingEnvelope
            {
                BindingId = bindingId,
                CreatedAt = createdAt,
                LabelXml = labelXml,
                Payload = payload,
                MediaType = protectedObject.Attribute("mediaType")?.Value
            },
            Label = decodeResult.IsSuccess ? decodeResult.Label : null
        };
    }
}

using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;

namespace AbacController.Pep;

/// <summary>
/// Metadata binding service implementing the STANAG 4778 Binding Data Object (BDO) format
/// as specified in ADatP-4778.2 Edition A Version 1 (December 2020).
///
/// BDO structure:
///   BindingInformation (urn:nato:stanag:4778:bindinginformation:1:0)
///     MetadataBindingContainer
///       MetadataBinding @xml:id
///         Metadata @xml:id
///           [STANAG 4774 label XML]
///         DataReference @URI="#do-{uuid}" @xmime:contentType
///     DataObject @xml:id @encoding="base64"
///       [base64-encoded payload]
///
/// Note: DataObject is a local extension element. ADatP-4778.2 has no defined element
/// for carrying inline binary payloads. Full spec conformance for binary data requires
/// Phase 1 detached binding (external URI + digest).
/// </summary>
public sealed class Stanag4778MetadataBinder : IStanag4778MetadataBinder
{
    private static readonly XNamespace BindingNs = SpifNamespaces.Stanag4778;
    private static readonly XNamespace XmlNs = XNamespace.Get("http://www.w3.org/XML/1998/namespace");
    private static readonly XNamespace XmimeNs = XNamespace.Get(SpifNamespaces.Xmime);

    private readonly ILabelCodec _labelCodec;

    /// <summary>Initializes a new instance of the <see cref="Stanag4778MetadataBinder"/> class.</summary>
    public Stanag4778MetadataBinder(IEnumerable<ILabelCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        _labelCodec = codecs.FirstOrDefault(static codec =>
                string.Equals(codec.CodecId, "stanag4774-xml", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("STANAG 4774 XML label codec is required for metadata binding.");
    }

    /// <summary>
    /// Binds a STANAG 4774 label and opaque payload into a conformant STANAG 4778 BDO.
    /// </summary>
    public string Bind(MetadataBindingEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.LabelXml);
        ArgumentNullException.ThrowIfNull(envelope.Payload);

        var labelDocument = XDocument.Parse(envelope.LabelXml, LoadOptions.PreserveWhitespace);
        var labelRoot = labelDocument.Root
            ?? throw new InvalidOperationException("Label XML is missing a root element.");

        // Generate xml:id values. MetadataBinding xml:id reuses caller's BindingId if provided
        // (caller is responsible for supplying a valid XML Name); otherwise generate a prefixed UUID.
        var bindingId = string.IsNullOrWhiteSpace(envelope.BindingId)
            ? "mb-" + Guid.NewGuid().ToString("N")
            : envelope.BindingId;
        var metadataId = "md-" + Guid.NewGuid().ToString("N");
        var dataObjectId = "do-" + Guid.NewGuid().ToString("N");

        var dataRefAttributes = new List<XAttribute>
        {
            new XAttribute("URI", "#" + dataObjectId)
        };
        if (!string.IsNullOrWhiteSpace(envelope.MediaType))
        {
            dataRefAttributes.Add(new XAttribute(XmimeNs + "contentType", envelope.MediaType));
        }

        var document = new XDocument(
            new XElement(BindingNs + "BindingInformation",
                new XAttribute(XNamespace.Xmlns + "mb", BindingNs),
                new XAttribute(XNamespace.Xmlns + "xmime", XmimeNs),
                new XElement(BindingNs + "MetadataBindingContainer",
                    new XElement(BindingNs + "MetadataBinding",
                        new XAttribute(XmlNs + "id", bindingId),
                        new XElement(BindingNs + "Metadata",
                            new XAttribute(XmlNs + "id", metadataId),
                            new XElement(labelRoot)),
                        new XElement(BindingNs + "DataReference", dataRefAttributes))),
                new XElement(BindingNs + "DataObject",
                    new XAttribute(XmlNs + "id", dataObjectId),
                    new XAttribute("encoding", "base64"),
                    Convert.ToBase64String(envelope.Payload))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Extracts the label and payload from a conformant STANAG 4778 BDO.
    /// </summary>
    public MetadataUnbindResult Unbind(string envelopeXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeXml);

        var document = XDocument.Parse(envelopeXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException("BDO XML is missing a root element.");

        return root.Name.LocalName switch
        {
            "BindingInformation" => UnbindBdo(root),
            _ => throw new InvalidOperationException(
                $"Unrecognised BDO root element '{root.Name.LocalName}'. " +
                $"Expected 'BindingInformation' in namespace '{SpifNamespaces.Stanag4778}'.")
        };
    }

    // Parses the conformant STANAG 4778 BDO format.
    private MetadataUnbindResult UnbindBdo(XElement root)
    {
        var binding = root
            .Elements().FirstOrDefault(static e => e.Name.LocalName == "MetadataBindingContainer")
            ?.Elements().FirstOrDefault(static e => e.Name.LocalName == "MetadataBinding")
            ?? throw new InvalidOperationException("BDO does not contain a MetadataBinding element.");

        var bindingId = binding.Attribute(XmlNs + "id")?.Value
            ?? binding.Attributes().FirstOrDefault(static a => a.Name.LocalName == "id")?.Value;

        var metadataElement = binding.Elements()
            .FirstOrDefault(static e => e.Name.LocalName == "Metadata")
            ?? throw new InvalidOperationException("MetadataBinding does not contain an embedded Metadata element.");

        var labelElement = metadataElement.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("Metadata element does not contain a security label.");

        var dataRef = binding.Elements()
            .FirstOrDefault(static e => e.Name.LocalName == "DataReference");

        // Resolve the DataReference URI to a DataObject in the BDO.
        var (payload, mediaType) = ResolveDataObject(root, dataRef);

        var labelXml = labelElement.ToString(SaveOptions.DisableFormatting);
        var decodeResult = _labelCodec.Decode(labelXml);

        return new MetadataUnbindResult
        {
            Envelope = new MetadataBindingEnvelope
            {
                BindingId = bindingId,
                LabelXml = labelXml,
                Payload = payload,
                MediaType = mediaType
            },
            Label = decodeResult.IsSuccess ? decodeResult.Label : null
        };
    }

    // Resolves a DataReference element to payload bytes and media type.
    private static (byte[] payload, string? mediaType) ResolveDataObject(XElement root, XElement? dataRef)
    {
        if (dataRef is null)
        {
            throw new InvalidOperationException("MetadataBinding does not contain a DataReference element.");
        }

        var uri = dataRef.Attribute("URI")?.Value ?? string.Empty;

        if (!uri.StartsWith('#'))
        {
            // External URI — not supported in Phase 0 (detached binding is Phase 1).
            throw new InvalidOperationException(
                $"DataReference URI '{uri}' is an external reference. " +
                "Only fragment references (same-document DataObject) are supported in this implementation.");
        }

        var refId = uri.TrimStart('#');
        // Search direct children of root only to avoid false matches inside nested XML payloads.
        var dataObject = root.Elements(BindingNs + "DataObject")
            .FirstOrDefault(e =>
                e.Attribute(XmlNs + "id")?.Value == refId ||
                e.Attributes().FirstOrDefault(static a => a.Name.LocalName == "id")?.Value == refId)
            ?? throw new InvalidOperationException(
                $"BDO does not contain a DataObject with xml:id='{refId}' referenced by DataReference URI='{uri}'.");

        var payloadBase64 = dataObject.Value.Trim();
        var payload = string.IsNullOrEmpty(payloadBase64) ? [] : Convert.FromBase64String(payloadBase64);

        var mediaType = dataRef.Attributes()
            .FirstOrDefault(static a => a.Name.LocalName == "contentType")?.Value;

        return (payload, mediaType);
    }
}

using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;

namespace AbacController.Pep;

/// <summary>
/// Metadata binding service implementing the STANAG 4778 Binding Data Object (BDO) format
/// as specified in ADatP-4778.2 Edition A Version 1 (December 2020).
///
/// BDO structure (inline / Phase 0):
///   BindingInformation (urn:nato:stanag:4778:bindinginformation:1:0)
///     MetadataBindingContainer
///       MetadataBinding @xml:id
///         Metadata @xml:id
///           [STANAG 4774 label XML]
///         DataReference @URI="#do-{uuid}" @xmime:contentType
///     DataObject @xml:id @encoding="base64"
///       [base64-encoded payload]
///
/// BDO structure (detached / Phase 1 — HTTP body binding):
///   BindingInformation
///     MetadataBindingContainer
///       MetadataBinding @xml:id
///         Metadata @xml:id
///           [STANAG 4774 label XML]
///         DataReference @URI="" @xmime:contentType="message/http"
///   (no DataObject — data is the HTTP entity body)
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
    /// The payload is carried inline as a base64-encoded DataObject element (Phase 0 / local extension).
    /// </summary>
    public string Bind(MetadataBindingEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.LabelXml);
        ArgumentNullException.ThrowIfNull(envelope.Payload);

        var labelDocument = XDocument.Parse(envelope.LabelXml, LoadOptions.PreserveWhitespace);
        var labelRoot = labelDocument.Root
            ?? throw new InvalidOperationException("Label XML is missing a root element.");

        var bindingId = ResolveBindingId(envelope.BindingId);
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
    /// Binds a STANAG 4774 label to an externally-located data object (no inline payload).
    /// Produces a BDO with <c>DataReference URI="{dataUri}"</c> and no DataObject element.
    /// For the HTTP entity body binding profile use <c>dataUri=""</c> and
    /// <c>contentType="message/http"</c> per ADatP-4778.2 Chapter 7.
    /// </summary>
    public string BindDetached(string labelXml, string dataUri, string? contentType, string? bindingId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelXml);
        ArgumentNullException.ThrowIfNull(dataUri); // empty string is valid (null URI = HTTP body)

        var labelDocument = XDocument.Parse(labelXml, LoadOptions.PreserveWhitespace);
        var labelRoot = labelDocument.Root
            ?? throw new InvalidOperationException("Label XML is missing a root element.");

        var resolvedBindingId = ResolveBindingId(bindingId);
        var metadataId = "md-" + Guid.NewGuid().ToString("N");

        var dataRefAttributes = new List<XAttribute>
        {
            new XAttribute("URI", dataUri)
        };
        // Whitespace-only contentType is treated as absent (same as null).
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            dataRefAttributes.Add(new XAttribute(XmimeNs + "contentType", contentType));
        }

        var document = new XDocument(
            new XElement(BindingNs + "BindingInformation",
                new XAttribute(XNamespace.Xmlns + "mb", BindingNs),
                new XAttribute(XNamespace.Xmlns + "xmime", XmimeNs),
                new XElement(BindingNs + "MetadataBindingContainer",
                    new XElement(BindingNs + "MetadataBinding",
                        new XAttribute(XmlNs + "id", resolvedBindingId),
                        new XElement(BindingNs + "Metadata",
                            new XAttribute(XmlNs + "id", metadataId),
                            new XElement(labelRoot)),
                        new XElement(BindingNs + "DataReference", dataRefAttributes)))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Extracts the label and payload from a conformant STANAG 4778 BDO.
    /// Supports inline DataObject (Phase 0) and null-URI detached binding (Phase 1).
    /// </summary>
    public MetadataUnbindResult Unbind(string envelopeXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeXml);

        var document = XDocument.Parse(envelopeXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException("BDO XML is missing a root element.");

        if (root.Name == BindingNs + "BindingInformation")
        {
            return UnbindBdo(root);
        }

        throw new InvalidOperationException(
            $"Unrecognised BDO root element '{root.Name.LocalName}' in namespace '{root.Name.NamespaceName}'. " +
            $"Expected 'BindingInformation' in namespace '{SpifNamespaces.Stanag4778}'.");
    }

    // Parses the conformant STANAG 4778 BDO format.
    private MetadataUnbindResult UnbindBdo(XElement root)
    {
        var binding = root
            .Elements().FirstOrDefault(static e => e.Name.LocalName == "MetadataBindingContainer")
            ?.Elements().FirstOrDefault(static e => e.Name.LocalName == "MetadataBinding")
            ?? throw new InvalidOperationException("BDO does not contain a MetadataBinding element.");

        var bindingId = binding.Attribute(XmlNs + "id")?.Value
            // Fallback: accept unqualified 'id' attribute from implementations that omit the xml: namespace prefix.
            ?? binding.Attributes().FirstOrDefault(static a => a.Name.LocalName == "id")?.Value;

        var metadataElement = binding.Elements()
            .FirstOrDefault(static e => e.Name.LocalName == "Metadata")
            ?? throw new InvalidOperationException("MetadataBinding does not contain an embedded Metadata element.");

        var labelElement = metadataElement.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("Metadata element does not contain a security label.");

        var dataRef = binding.Elements()
            .FirstOrDefault(static e => e.Name.LocalName == "DataReference");

        // Resolve the DataReference URI to a payload.
        var (payload, mediaType) = ResolvePayload(root, dataRef);

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
    // Supports:
    //   URI=""      → null-URI / HTTP entity body binding (Phase 1): returns empty payload
    //   URI="#id"   → same-document DataObject reference (Phase 0): decodes inline base64
    //   URI="other" → external reference (Phase 2+): throws InvalidOperationException
    private static (byte[] payload, string? mediaType) ResolvePayload(XElement root, XElement? dataRef)
    {
        if (dataRef is null)
        {
            throw new InvalidOperationException("MetadataBinding does not contain a DataReference element.");
        }

        var uri = dataRef.Attribute("URI")?.Value ?? string.Empty;
        var mediaType = dataRef.Attributes().FirstOrDefault(static a => a.Name.LocalName == "contentType")?.Value;

        if (uri.Length == 0)
        {
            // Null-URI (URI=""): detached binding — data is carried in the transport layer (e.g., HTTP entity body).
            // Return empty payload; the caller's transport context carries the actual data.
            return ([], mediaType);
        }

        if (uri.StartsWith('#'))
        {
            // Fragment reference: same-document DataObject (Phase 0 local extension).
            return ResolveDataObject(root, uri, mediaType);
        }

        // Non-empty, non-fragment URI: external reference requires a Manifest/DigestValue (Phase 2 XMLDSIG).
        throw new InvalidOperationException(
            $"DataReference URI '{uri}' is an external reference. " +
            "Only fragment (#) and null (\"\") URI references are supported. " +
            "External URI binding requires Phase 2 XMLDSIG Manifest support.");
    }

    // Resolves a fragment URI to a sibling DataObject element and decodes its base64 payload.
    private static (byte[] payload, string? mediaType) ResolveDataObject(XElement root, string fragmentUri, string? mediaType)
    {
        var refId = fragmentUri.TrimStart('#');
        // Search direct children of root only to avoid false matches inside nested XML payloads.
        var dataObject = root.Elements(BindingNs + "DataObject")
            .FirstOrDefault(e =>
                e.Attribute(XmlNs + "id")?.Value == refId ||
                // Fallback: accept unqualified 'id' attribute from implementations that omit the xml: namespace prefix.
                e.Attributes().FirstOrDefault(static a => a.Name.LocalName == "id")?.Value == refId)
            ?? throw new InvalidOperationException(
                $"BDO does not contain a DataObject with xml:id='{refId}' referenced by DataReference URI='{fragmentUri}'.");

        var payloadBase64 = dataObject.Value.Trim();
        var payload = string.IsNullOrEmpty(payloadBase64) ? [] : Convert.FromBase64String(payloadBase64);

        return (payload, mediaType);
    }

    // Validates and returns the bindingId, auto-generating a prefixed UUID if null/empty.
    private static string ResolveBindingId(string? bindingId)
    {
        if (string.IsNullOrWhiteSpace(bindingId))
        {
            return "mb-" + Guid.NewGuid().ToString("N");
        }

        try { System.Xml.XmlConvert.VerifyNCName(bindingId); }
        catch (System.Xml.XmlException ex)
        {
            throw new ArgumentException(
                $"BindingId '{bindingId}' is not a valid XML NCName and cannot be used as xml:id. " +
                "Use only letters, digits, hyphens, underscores, and dots; the first character must be a letter or underscore.",
                nameof(bindingId), ex);
        }

        return bindingId;
    }
}

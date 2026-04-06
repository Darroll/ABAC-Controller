using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep;
using AbacController.Pep.Codecs.Xml;
// SpifIndex is in AbacController.Pdp; no ISpifIndex reference needed (using concrete type)

namespace AbacController.Tests.Unit;

public sealed class Stanag4778MetadataBinderTests
{
    // Shared test fixtures
    private static readonly XNamespace BindingNs = SpifNamespaces.Stanag4778;
    private static readonly XNamespace XmlNs = "http://www.w3.org/XML/1998/namespace";
    private static readonly XNamespace XmimeNs = SpifNamespaces.Xmime;

    private static (Stanag4778MetadataBinder binder, SpifIndex spifIndex, XmlStanag4774Codec codec) CreateBinder()
    {
        var parser = new SpifParser();
        var parsed = parser.Parse(TestSpifSamples.BasicPolicy);
        var spifIndex = new SpifIndex(parsed.Spif!);
        var codec = new XmlStanag4774Codec();
        var binder = new Stanag4778MetadataBinder([codec]);
        return (binder, spifIndex, codec);
    }

    private static MetadataBindingEnvelope BuildEnvelope(string labelXml, string? bindingId = null) =>
        new()
        {
            BindingId = bindingId ?? "bind-001",
            LabelXml = labelXml,
            Payload = Encoding.UTF8.GetBytes("hello world"),
            MediaType = "text/plain"
        };

    // --- BDO structure tests ---

    [Fact]
    public void Bind_Produces_BindingInformation_Root_Element()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);
        Assert.True(encoded.IsSuccess, encoded.Error);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));
        var doc = XDocument.Parse(xml);

        Assert.Equal("BindingInformation", doc.Root!.Name.LocalName);
        Assert.Equal(SpifNamespaces.Stanag4778, doc.Root.Name.NamespaceName);
    }

    [Fact]
    public void Bind_Produces_Correct_BDO_Namespace()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));

        Assert.Contains("urn:nato:stanag:4778:bindinginformation:1:0", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("metadatabindingmechanism", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_Produces_MetadataBindingContainer_With_MetadataBinding()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        var container = root.Element(BindingNs + "MetadataBindingContainer");
        Assert.NotNull(container);

        var binding = container.Element(BindingNs + "MetadataBinding");
        Assert.NotNull(binding);
    }

    [Fact]
    public void Bind_MetadataBinding_Has_XmlId_Attribute()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!, bindingId: "bind-001"));
        var doc = XDocument.Parse(xml);

        var binding = doc.Root!
            .Element(BindingNs + "MetadataBindingContainer")!
            .Element(BindingNs + "MetadataBinding");

        var xmlId = binding!.Attribute(XmlNs + "id")?.Value;
        Assert.Equal("bind-001", xmlId);
    }

    [Fact]
    public void Bind_AutoGenerates_XmlId_When_BindingId_Null()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(new MetadataBindingEnvelope
        {
            LabelXml = encoded.EncodedString!,
            Payload = Encoding.UTF8.GetBytes("data"),
            BindingId = null
        });
        var doc = XDocument.Parse(xml);

        var binding = doc.Root!
            .Element(BindingNs + "MetadataBindingContainer")!
            .Element(BindingNs + "MetadataBinding");

        var xmlId = binding!.Attribute(XmlNs + "id")?.Value;
        Assert.NotNull(xmlId);
        Assert.NotEmpty(xmlId);
        // Auto-generated IDs are prefixed to ensure valid XML Name (no leading digit)
        Assert.True(char.IsLetter(xmlId[0]) || xmlId[0] == '_',
            $"xml:id must start with a letter or underscore, got: {xmlId}");
    }

    [Fact]
    public void Bind_Metadata_Element_Has_XmlId_And_Contains_Label()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));
        var doc = XDocument.Parse(xml);

        var metadata = doc.Root!
            .Element(BindingNs + "MetadataBindingContainer")!
            .Element(BindingNs + "MetadataBinding")!
            .Element(BindingNs + "Metadata");

        Assert.NotNull(metadata);
        Assert.NotNull(metadata!.Attribute(XmlNs + "id"));
        Assert.Single(metadata.Elements()); // exactly one label child
        Assert.Equal("originatorConfidentialityLabel", metadata.Elements().First().Name.LocalName);
    }

    [Fact]
    public void Bind_Produces_DataReference_Pointing_To_DataObject()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));
        var doc = XDocument.Parse(xml);

        var binding = doc.Root!
            .Element(BindingNs + "MetadataBindingContainer")!
            .Element(BindingNs + "MetadataBinding");

        var dataRef = binding!.Element(BindingNs + "DataReference");
        Assert.NotNull(dataRef);

        var uri = dataRef!.Attribute("URI")?.Value;
        Assert.NotNull(uri);
        Assert.StartsWith("#", uri);

        // The referenced DataObject must exist at the BDO level
        var refId = uri!.TrimStart('#');
        // Direct children of root only — verifies structural invariant that DataObject is
        // a sibling of MetadataBindingContainer, not nested inside it.
        var dataObject = doc.Root!.Elements(BindingNs + "DataObject")
            .FirstOrDefault(e => e.Attribute(XmlNs + "id")?.Value == refId);
        Assert.NotNull(dataObject);
    }

    [Fact]
    public void Bind_DataReference_Carries_ContentType()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));
        var doc = XDocument.Parse(xml);

        var dataRef = doc.Root!
            .Element(BindingNs + "MetadataBindingContainer")!
            .Element(BindingNs + "MetadataBinding")!
            .Element(BindingNs + "DataReference");

        var contentType = dataRef!.Attribute(XmimeNs + "contentType")?.Value;
        Assert.Equal("text/plain", contentType);
    }

    [Fact]
    public void Bind_DataObject_Contains_Base64_Payload()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(BuildEnvelope(encoded.EncodedString!));
        var doc = XDocument.Parse(xml);

        var dataObject = doc.Root!.Elements(BindingNs + "DataObject").FirstOrDefault();
        Assert.NotNull(dataObject);

        var payloadBytes = Convert.FromBase64String(dataObject!.Value.Trim());
        Assert.Equal("hello world", Encoding.UTF8.GetString(payloadBytes));
    }

    // --- Round-trip tests ---

    [Fact]
    public void Bind_Unbind_RoundTrips_Label_And_Payload()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);
        Assert.True(encoded.IsSuccess, encoded.Error);

        var envelopeXml = binder.Bind(new MetadataBindingEnvelope
        {
            BindingId = "bind-001",
            LabelXml = encoded.EncodedString!,
            Payload = Encoding.UTF8.GetBytes("hello world"),
            MediaType = "text/plain"
        });

        var unbound = binder.Unbind(envelopeXml);

        Assert.Equal("bind-001", unbound.Envelope.BindingId);
        Assert.Equal("text/plain", unbound.Envelope.MediaType);
        Assert.Equal("hello world", Encoding.UTF8.GetString(unbound.Envelope.Payload));
        Assert.NotNull(unbound.Label);
        Assert.Equal("1.2.3.4", unbound.Label!.PolicyOid);
        Assert.Equal(3, unbound.Label.ClassificationLacv.Value);
    }

    [Fact]
    public void Unbind_Returns_AutoGenerated_BindingId_When_BindingId_Is_Null()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var envelopeXml = binder.Bind(new MetadataBindingEnvelope
        {
            BindingId = null,
            LabelXml = encoded.EncodedString!,
            Payload = Encoding.UTF8.GetBytes("data")
        });

        var unbound = binder.Unbind(envelopeXml);
        // Auto-generated xml:id is returned; it is not null (it's the generated ID)
        Assert.NotNull(unbound.Envelope.BindingId);
    }

    [Fact]
    public void Unbind_Throws_When_Metadata_Missing()
    {
        var (binder, _, _) = CreateBinder();

        // BDO with no Metadata child
        var badXml = """
            <mb:BindingInformation xmlns:mb="urn:nato:stanag:4778:bindinginformation:1:0">
              <mb:MetadataBindingContainer>
                <mb:MetadataBinding xml:id="mb-test">
                  <mb:DataReference URI="#do-test"/>
                </mb:MetadataBinding>
              </mb:MetadataBindingContainer>
            </mb:BindingInformation>
            """;

        Assert.Throws<InvalidOperationException>(() => binder.Unbind(badXml));
    }

    [Fact]
    public void Unbind_Throws_When_DataObject_Missing()
    {
        var (binder, _, _) = CreateBinder();

        // BDO with DataReference but no DataObject
        var badXml = """
            <mb:BindingInformation xmlns:mb="urn:nato:stanag:4778:bindinginformation:1:0">
              <mb:MetadataBindingContainer>
                <mb:MetadataBinding xml:id="mb-test">
                  <mb:Metadata xml:id="md-test">
                    <label/>
                  </mb:Metadata>
                  <mb:DataReference URI="#do-missing"/>
                </mb:MetadataBinding>
              </mb:MetadataBindingContainer>
            </mb:BindingInformation>
            """;

        Assert.Throws<InvalidOperationException>(() => binder.Unbind(badXml));
    }

    [Fact]
    public void Unbind_Throws_When_Root_Is_BindingInformation_In_Wrong_Namespace()
    {
        var (binder, _, _) = CreateBinder();

        // BindingInformation in the wrong namespace should be rejected
        var badXml = """
            <x:BindingInformation xmlns:x="urn:example.com/wrong-namespace">
              <x:MetadataBindingContainer/>
            </x:BindingInformation>
            """;

        Assert.Throws<InvalidOperationException>(() => binder.Unbind(badXml));
    }

    [Fact]
    public void Unbind_Throws_When_DataReference_Uses_External_Uri()
    {
        var (binder, _, _) = CreateBinder();

        // External DataReference URI is not supported in Phase 0
        var badXml = """
            <mb:BindingInformation xmlns:mb="urn:nato:stanag:4778:bindinginformation:1:0">
              <mb:MetadataBindingContainer>
                <mb:MetadataBinding xml:id="mb-test">
                  <mb:Metadata xml:id="md-test">
                    <label/>
                  </mb:Metadata>
                  <mb:DataReference URI="http://example.com/data.bin"/>
                </mb:MetadataBinding>
              </mb:MetadataBindingContainer>
            </mb:BindingInformation>
            """;

        Assert.Throws<InvalidOperationException>(() => binder.Unbind(badXml));
    }

    [Fact]
    public void Bind_DataReference_Has_No_ContentType_When_MediaType_Is_Null()
    {
        var (binder, spifIndex, codec) = CreateBinder();
        var label = MakeLabel();
        var encoded = codec.Encode(label, spifIndex);

        var xml = binder.Bind(new MetadataBindingEnvelope
        {
            LabelXml = encoded.EncodedString!,
            Payload = Encoding.UTF8.GetBytes("data"),
            MediaType = null
        });
        var doc = XDocument.Parse(xml);

        var dataRef = doc.Root!
            .Element(BindingNs + "MetadataBindingContainer")!
            .Element(BindingNs + "MetadataBinding")!
            .Element(BindingNs + "DataReference");

        Assert.Null(dataRef!.Attribute(XmimeNs + "contentType"));
    }

    // --- Helper ---

    private static SecurityLabel MakeLabel() => new()
    {
        PolicyOid = "1.2.3.4",
        PolicyName = "TEST",
        ClassificationLacv = 3,
        ClassificationName = "SECRET",
        CategoryTagSets = ImmutableList.Create(new LabelCategoryTagSet
        {
            TagSetOid = "1.2.3.4.1",
            Tags = ImmutableList.Create(new LabelCategoryTag
            {
                Name = "SCI",
                TagType = TagType.Restrictive,
                Bits = ImmutableHashSet.Create<LacvValue>(10),
                Categories = ImmutableList.Create(new LabelCategory { Name = "ALPHA", Lacv = 10 })
            })
        })
    };
}

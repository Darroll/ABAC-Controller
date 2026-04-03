using System.Collections.Immutable;
using System.Text;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep;
using AbacController.Pep.Codecs.Xml;

namespace AbacController.Tests.Unit;

public sealed class Stanag4778MetadataBinderTests
{
    [Fact]
    public void Bind_Unbind_RoundTrips_Label_And_Payload()
    {
        var parser = new SpifParser();
        var parsed = parser.Parse(TestSpifSamples.BasicPolicy);
        Assert.True(parsed.Success, string.Join(" | ", parsed.Errors.Select(e => e.Message)));

        var spifIndex = new SpifIndex(parsed.Spif!);
        var codec = new XmlStanag4774Codec();
        var binder = new Stanag4778MetadataBinder([codec]);

        var label = new SecurityLabel
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

        var encoded = codec.Encode(label, spifIndex);
        Assert.True(encoded.IsSuccess, encoded.Error);

        var envelopeXml = binder.Bind(new MetadataBindingEnvelope
        {
            BindingId = "bind-001",
            CreatedAt = DateTimeOffset.Parse("2026-04-03T19:45:00Z"),
            LabelXml = encoded.EncodedString!,
            Payload = Encoding.UTF8.GetBytes("hello world"),
            MediaType = "text/plain"
        });

        Assert.Contains("MetadataBindingEnvelope", envelopeXml, StringComparison.Ordinal);
        Assert.Contains("bind-001", envelopeXml, StringComparison.Ordinal);

        var unbound = binder.Unbind(envelopeXml);

        Assert.Equal("bind-001", unbound.Envelope.BindingId);
        Assert.Equal("text/plain", unbound.Envelope.MediaType);
        Assert.Equal("hello world", Encoding.UTF8.GetString(unbound.Envelope.Payload));
        Assert.NotNull(unbound.Label);
        Assert.Equal("1.2.3.4", unbound.Label!.PolicyOid);
        Assert.Equal(3, unbound.Label.ClassificationLacv.Value);
    }
}

using System.Collections.Immutable;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep.Codecs.Xml;

namespace AbacController.Tests.Unit;

public sealed class XmlStanag4774CodecTests
{
    [Fact]
    public void EncodeDecode_Preserves_CanonicalSecurityValues()
    {
        var parser = new SpifParser();
        var parsed = parser.Parse(TestSpifSamples.BasicPolicy);
        Assert.True(parsed.Success, string.Join(" | ", parsed.Errors.Select(e => e.Message)));
        var spifIndex = new SpifIndex(parsed.Spif!);
        var codec = new XmlStanag4774Codec();

        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4",
            PolicyName = "TEST",
            ClassificationLacv = 3,
            ClassificationName = "SECRET",
            CreatedAt = DateTimeOffset.Parse("2026-04-03T12:30:00Z"),
            CategoryTagSets = ImmutableList.Create(new LabelCategoryTagSet
            {
                TagSetOid = "1.2.3.4.1",
                Tags = ImmutableList.Create(
                    new LabelCategoryTag
                    {
                        Name = "SCI",
                        TagType = TagType.Restrictive,
                        Bits = ImmutableHashSet.Create<LacvValue>(10),
                        Categories = ImmutableList.Create(new LabelCategory { Name = "ALPHA", Lacv = 10 })
                    },
                    new LabelCategoryTag
                    {
                        Name = "REL TO",
                        TagType = TagType.Enumerated,
                        EnumType = EnumType.Permissive,
                        EnumeratedValues = ImmutableHashSet.Create<LacvValue>(20),
                        Categories = ImmutableList.Create(new LabelCategory { Name = "USA", Lacv = 20 })
                    })
            })
        };

        var encoded = codec.Encode(label, spifIndex);
        Assert.True(encoded.IsSuccess, encoded.Error);
        Assert.Contains("oid=\"1.2.3.4\"", encoded.EncodedString);
        Assert.Contains("lacv=\"3\"", encoded.EncodedString);
        Assert.Contains("TagSetOid=\"1.2.3.4.1\"", encoded.EncodedString);
        Assert.Contains("EnumType=\"permissive\"", encoded.EncodedString);

        var decoded = codec.Decode(encoded.EncodedString!);
        Assert.True(decoded.IsSuccess, decoded.Error);
        Assert.NotNull(decoded.Label);
        Assert.Equal("1.2.3.4", decoded.Label!.PolicyOid);
        Assert.Equal(3, decoded.Label.ClassificationLacv.Value);
        Assert.Equal("1.2.3.4.1", decoded.Label.CategoryTagSets.Single().TagSetOid);

        var relToTag = decoded.Label.CategoryTagSets.Single().Tags.Single(t => t.Name == "REL TO");
        Assert.Equal(TagType.Enumerated, relToTag.TagType);
        Assert.Equal(EnumType.Permissive, relToTag.EnumType);
        Assert.Contains((LacvValue)20, relToTag.EnumeratedValues);
    }
}

using System.Collections.Immutable;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep;
using AbacController.Pep.Codecs.Xml;

namespace AbacController.Tests.Integration;

public sealed class EndToEndStandardsFlowTests
{
    [Fact]
    public void Parse_Encode_Decode_Validate_And_Evaluate_Succeeds_For_ValidLabel()
    {
        var parser = new SpifParser();
        var parsed = parser.Parse(TestSpifSamples.BasicPolicy);
        Assert.True(parsed.Success, string.Join(" | ", parsed.Errors.Select(e => e.Message)));

        var spifIndex = new SpifIndex(parsed.Spif!);
        var codec = new XmlStanag4774Codec();
        var validator = new LabelValidator();
        var evaluator = new AcdfEvaluator();

        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4",
            PolicyName = "TEST",
            ClassificationLacv = 3,
            ClassificationName = "SECRET",
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

        var decoded = codec.Decode(encoded.EncodedString!);
        Assert.True(decoded.IsSuccess, decoded.Error);

        var validation = validator.Validate(decoded.Label!, spifIndex);
        Assert.True(validation.IsValid, string.Join(" | ", validation.Errors));

        var clearance = new SecurityClearance
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacvs = ImmutableHashSet.Create<LacvValue>(3),
            CategoryTagSets = ImmutableList.Create(new ClearanceCategoryTagSet
            {
                TagSetOid = "1.2.3.4.1",
                Tags = ImmutableList.Create(
                    new ClearanceCategoryTag
                    {
                        TagOid = "SCI",
                        TagType = TagType.Restrictive,
                        Bits = ImmutableHashSet.Create<LacvValue>(10)
                    },
                    new ClearanceCategoryTag
                    {
                        TagOid = "REL TO",
                        TagType = TagType.Enumerated,
                        EnumeratedValues = ImmutableHashSet.Create<LacvValue>(20)
                    })
            })
        };

        var result = evaluator.Evaluate(decoded.Label!, clearance, spifIndex);
        Assert.True(result.Pass, result.FailureDetail);
    }
}

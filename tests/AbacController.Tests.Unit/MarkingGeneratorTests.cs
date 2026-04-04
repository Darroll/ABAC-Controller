using System.Collections.Immutable;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Pdp;
using AbacController.Pep;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for MarkingGenerator — classification marking string generation.
/// </summary>
public sealed class MarkingGeneratorTests
{
    private static ISpifIndex MakeSpifIndex()
    {
        var spif = new Spif
        {
            SchemaVersion = "2.1",
            PolicyId = new PolicyInfo { Name = "TEST POLICY", Oid = "1.2.3.4.5" },
            Classifications =
            [
                new SecurityClassification { Name = "UNCLASSIFIED", Lacv = 0, Hierarchy = 0 },
                new SecurityClassification { Name = "CONFIDENTIAL", Lacv = 1, Hierarchy = 1 },
                new SecurityClassification { Name = "SECRET", Lacv = 2, Hierarchy = 2 },
                new SecurityClassification { Name = "TOP SECRET", Lacv = 3, Hierarchy = 3 }
            ],
            CategoryTagSets = [],
            EquivalentPolicies = []
        };
        return new SpifIndex(spif);
    }

    [Fact]
    public void GenerateMarking_UnclassifiedLabel_ContainsUnclassified()
    {
        var generator = new MarkingGenerator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 0,
            ClassificationName = "UNCLASSIFIED"
        };

        var marking = generator.GenerateMarking(label, spif);
        Assert.Contains("UNCLASSIFIED", marking);
    }

    [Fact]
    public void GenerateMarking_SecretLabel_ContainsSecret()
    {
        var generator = new MarkingGenerator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 2,
            ClassificationName = "SECRET"
        };

        var marking = generator.GenerateMarking(label, spif);
        Assert.Contains("SECRET", marking);
    }

    [Fact]
    public void GenerateShortMarking_ReturnsName_WhenNoShortPhraseExists()
    {
        var generator = new MarkingGenerator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 1,
            ClassificationName = "CONFIDENTIAL"
        };

        var marking = generator.GenerateShortMarking(label, spif);
        Assert.Contains("CONFIDENTIAL", marking);
    }

    [Fact]
    public void GenerateMarking_UnknownClassificationLacv_FallsBackToName()
    {
        var generator = new MarkingGenerator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 99,
            ClassificationName = "CUSTOM"
        };

        var marking = generator.GenerateMarking(label, spif);
        Assert.Contains("CUSTOM", marking);
    }

    [Fact]
    public void GenerateMarking_EmptyCategoryTagSets_OnlyClassification()
    {
        var generator = new MarkingGenerator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 3,
            ClassificationName = "TOP SECRET",
            CategoryTagSets = ImmutableList<LabelCategoryTagSet>.Empty
        };

        var marking = generator.GenerateMarking(label, spif);
        Assert.Equal("TOP SECRET", marking);
    }
}

using System.Collections.Immutable;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Pdp;
using AbacController.Pep;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for LabelValidator — security label validation against SPIF.
/// </summary>
public sealed class LabelValidatorTests
{
    private static SpifIndex MakeSpifIndex()
    {
        var spif = new Spif
        {
            SchemaVersion = "2.1",
            PolicyId = new PolicyInfo { Name = "TEST", Oid = "1.2.3.4.5" },
            Classifications =
            [
                new SecurityClassification { Name = "UNCLASSIFIED", Lacv = 0, Hierarchy = 0 },
                new SecurityClassification { Name = "SECRET", Lacv = 2, Hierarchy = 2 },
            ],
            CategoryTagSets = [],
            EquivalentPolicies = []
        };
        return new SpifIndex(spif);
    }

    [Fact]
    public void Validate_ValidLabel_Succeeds()
    {
        var validator = new LabelValidator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 0,
            ClassificationName = "UNCLASSIFIED"
        };

        var result = validator.Validate(label, spif);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownClassification_Fails()
    {
        var validator = new LabelValidator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 99,
            ClassificationName = "BOGUS"
        };

        var result = validator.Validate(label, spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not found in SPIF"));
    }

    [Fact]
    public void Validate_ValidSecretLabel_Succeeds()
    {
        var validator = new LabelValidator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 2,
            ClassificationName = "SECRET"
        };

        var result = validator.Validate(label, spif);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownTagSetOid_Fails()
    {
        var validator = new LabelValidator();
        var spif = MakeSpifIndex();
        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4.5",
            ClassificationLacv = 0,
            ClassificationName = "UNCLASSIFIED",
            CategoryTagSets =
            [
                new LabelCategoryTagSet
                {
                    TagSetOid = "9.9.9.9.9",
                    Tags = []
                }
            ]
        };

        var result = validator.Validate(label, spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Tag set OID"));
    }
}

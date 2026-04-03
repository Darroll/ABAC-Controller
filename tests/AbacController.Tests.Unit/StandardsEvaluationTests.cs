using System.Collections.Immutable;
using AbacController.Core.Domain.Labels;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep;

namespace AbacController.Tests.Unit;

public sealed class StandardsEvaluationTests
{
    private static readonly SpifIndex SpifIndex = CreateIndex();

    [Fact]
    public void LabelValidator_Rejects_OnlyOneConstraint_When_MultipleCategoriesPresent()
    {
        var validator = new LabelValidator();
        var label = CreateSecretLabel(10, 11);

        var result = validator.Validate(label, SpifIndex);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("onlyOne", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AcdfEvaluator_Rejects_Label_When_OnlyOneConstraintViolated()
    {
        var evaluator = new AcdfEvaluator();
        var label = CreateSecretLabel(10, 11);
        var clearance = CreateClearance(10, 20);

        var result = evaluator.Evaluate(label, clearance, SpifIndex);

        Assert.False(result.Pass);
        Assert.Contains("onlyOne", result.FailureDetail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcdfEvaluator_Passes_EndToEnd_For_ValidSecretLabel()
    {
        var evaluator = new AcdfEvaluator();
        var label = CreateSecretLabel(10, releaseTo: 20);
        var clearance = CreateClearance(10, 20);

        var result = evaluator.Evaluate(label, clearance, SpifIndex);

        Assert.True(result.Pass, result.FailureDetail);
    }

    private static SpifIndex CreateIndex()
    {
        var parser = new SpifParser();
        var parsed = parser.Parse(TestSpifSamples.BasicPolicy);
        Assert.True(parsed.Success, string.Join(" | ", parsed.Errors.Select(e => e.Message)));
        return new SpifIndex(parsed.Spif!);
    }

    private static SecurityLabel CreateSecretLabel(int compartment, int? secondCompartment = null, int? releaseTo = null)
    {
        var sciCategories = ImmutableList.CreateBuilder<LabelCategory>();
        var sciBits = ImmutableHashSet.CreateBuilder<AbacController.Core.Domain.Spif.LacvValue>();
        sciCategories.Add(new LabelCategory { Name = compartment == 10 ? "ALPHA" : "BRAVO", Lacv = compartment });
        sciBits.Add(compartment);

        if (secondCompartment is not null)
        {
            sciCategories.Add(new LabelCategory { Name = secondCompartment == 10 ? "ALPHA" : "BRAVO", Lacv = secondCompartment.Value });
            sciBits.Add(secondCompartment.Value);
        }

        var tags = ImmutableList.CreateBuilder<LabelCategoryTag>();
        tags.Add(new LabelCategoryTag
        {
            Name = "SCI",
            TagType = AbacController.Core.Domain.Spif.TagType.Restrictive,
            Bits = sciBits.ToImmutable(),
            Categories = sciCategories.ToImmutable()
        });

        if (releaseTo is not null)
        {
            tags.Add(new LabelCategoryTag
            {
                Name = "REL TO",
                TagType = AbacController.Core.Domain.Spif.TagType.Enumerated,
                EnumType = AbacController.Core.Domain.Spif.EnumType.Permissive,
                EnumeratedValues = ImmutableHashSet.Create<AbacController.Core.Domain.Spif.LacvValue>(releaseTo.Value),
                Categories = ImmutableList.Create(new LabelCategory { Name = releaseTo == 20 ? "USA" : "GBR", Lacv = releaseTo.Value })
            });
        }

        return new SecurityLabel
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 3,
            ClassificationName = "SECRET",
            CategoryTagSets = ImmutableList.Create(new LabelCategoryTagSet
            {
                TagSetOid = "1.2.3.4.1",
                Tags = tags.ToImmutable()
            })
        };
    }

    private static SecurityClearance CreateClearance(int compartment, int releaseTo)
        => new()
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacvs = ImmutableHashSet.Create<AbacController.Core.Domain.Spif.LacvValue>(3),
            CategoryTagSets = ImmutableList.Create(new ClearanceCategoryTagSet
            {
                TagSetOid = "1.2.3.4.1",
                Tags = ImmutableList.Create(
                    new ClearanceCategoryTag
                    {
                        TagOid = "SCI",
                        TagType = AbacController.Core.Domain.Spif.TagType.Restrictive,
                        Bits = ImmutableHashSet.Create<AbacController.Core.Domain.Spif.LacvValue>(compartment)
                    },
                    new ClearanceCategoryTag
                    {
                        TagOid = "REL TO",
                        TagType = AbacController.Core.Domain.Spif.TagType.Enumerated,
                        EnumeratedValues = ImmutableHashSet.Create<AbacController.Core.Domain.Spif.LacvValue>(releaseTo)
                    })
            })
        };
}

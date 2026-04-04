using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;
using AbacController.Pap;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for comprehensive SPIF validation beyond parse-time checks.
/// </summary>
public sealed class SpifValidatorTests
{
    private static Spif MakeValidSpif() => new()
    {
        SchemaVersion = "3.0",
        PolicyId = new PolicyInfo
        {
            Name = "NATO",
            Oid = "2.16.840.1.101.2.1.3.13.1",
            MarkingData = ImmutableList.Create(new MarkingData { Phrase = "NATO", Codes = ImmutableList.Create("pageTop") })
        },
        Classifications = ImmutableList.Create(
            new SecurityClassification
            {
                Name = "UNCLASSIFIED",
                Lacv = 0,
                Hierarchy = 0,
                MarkingData = ImmutableList.Create(new MarkingData { Phrase = "UNCLASSIFIED", Codes = ImmutableList.Create("pageTop") })
            },
            new SecurityClassification
            {
                Name = "CONFIDENTIAL",
                Lacv = 1,
                Hierarchy = 1,
                MarkingData = ImmutableList.Create(new MarkingData { Phrase = "CONFIDENTIAL", Codes = ImmutableList.Create("pageTop") })
            },
            new SecurityClassification
            {
                Name = "SECRET",
                Lacv = 2,
                Hierarchy = 2,
                MarkingData = ImmutableList.Create(new MarkingData { Phrase = "SECRET", Codes = ImmutableList.Create("pageTop") })
            }
        ),
        CategoryTagSets = ImmutableList.Create(
            new SecurityCategoryTagSet
            {
                TagSetOid = "2.16.840.1.101.2.1.8.3.1",
                Name = "REL TO",
                Tags = ImmutableList.Create(
                    new SecurityCategoryTag
                    {
                        Name = "Release Markings",
                        TagType = TagType.Permissive,
                        Categories = ImmutableList.Create(
                            new TagCategory { Name = "USA", Lacv = 1, MarkingData = ImmutableList.Create(new MarkingData { Phrase = "USA", Codes = ImmutableList.Create("pageTop") }) },
                            new TagCategory { Name = "GBR", Lacv = 2, MarkingData = ImmutableList.Create(new MarkingData { Phrase = "GBR", Codes = ImmutableList.Create("pageTop") }) },
                            new TagCategory { Name = "CAN", Lacv = 4, MarkingData = ImmutableList.Create(new MarkingData { Phrase = "CAN", Codes = ImmutableList.Create("pageTop") }) }
                        )
                    }
                )
            }
        )
    };

    [Fact]
    public void Validate_ValidSpif_ReturnsValid()
    {
        var result = SpifValidator.Validate(MakeValidSpif());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EmptyPolicyOid_Error()
    {
        var spif = MakeValidSpif() with
        {
            PolicyId = new PolicyInfo { Name = "Test", Oid = "" }
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Policy OID"));
    }

    [Fact]
    public void Validate_InvalidPolicyOid_Error()
    {
        var spif = MakeValidSpif() with
        {
            PolicyId = new PolicyInfo { Name = "Test", Oid = "not-an-oid" }
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("valid ASN.1 OID"));
    }

    [Fact]
    public void Validate_NoClassifications_Error()
    {
        var spif = MakeValidSpif() with { Classifications = ImmutableList<SecurityClassification>.Empty };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one"));
    }

    [Fact]
    public void Validate_DuplicateClassificationName_Error()
    {
        var spif = MakeValidSpif() with
        {
            Classifications = ImmutableList.Create(
                new SecurityClassification { Name = "SECRET", Lacv = 1, Hierarchy = 1 },
                new SecurityClassification { Name = "SECRET", Lacv = 2, Hierarchy = 2 }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate classification name"));
    }

    [Fact]
    public void Validate_DuplicateClassificationLacv_Error()
    {
        var spif = MakeValidSpif() with
        {
            Classifications = ImmutableList.Create(
                new SecurityClassification { Name = "UNCLASSIFIED", Lacv = 0, Hierarchy = 0 },
                new SecurityClassification { Name = "SECRET", Lacv = 0, Hierarchy = 2 }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate classification LACV"));
    }

    [Fact]
    public void Validate_AllClassificationsObsolete_Warning()
    {
        var spif = MakeValidSpif() with
        {
            Classifications = ImmutableList.Create(
                new SecurityClassification { Name = "OLD", Lacv = 0, Hierarchy = 0, Obsolete = true }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.Warnings, w => w.Contains("All classifications are marked obsolete"));
    }

    [Fact]
    public void Validate_DuplicateTagSetOid_Error()
    {
        var spif = MakeValidSpif() with
        {
            CategoryTagSets = ImmutableList.Create(
                new SecurityCategoryTagSet { TagSetOid = "2.16.840.1.1", Name = "Set1", Tags = ImmutableList.Create(new SecurityCategoryTag { Name = "T1", TagType = TagType.Permissive, Categories = ImmutableList.Create(new TagCategory { Name = "C1", Lacv = 1 }) }) },
                new SecurityCategoryTagSet { TagSetOid = "2.16.840.1.1", Name = "Set2", Tags = ImmutableList.Create(new SecurityCategoryTag { Name = "T2", TagType = TagType.Permissive, Categories = ImmutableList.Create(new TagCategory { Name = "C2", Lacv = 2 }) }) }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate tag set OID"));
    }

    [Fact]
    public void Validate_EnumeratedTagWithoutEnumType_Error()
    {
        var spif = MakeValidSpif() with
        {
            CategoryTagSets = ImmutableList.Create(
                new SecurityCategoryTagSet
                {
                    TagSetOid = "2.16.840.1.1",
                    Name = "Test",
                    Tags = ImmutableList.Create(
                        new SecurityCategoryTag
                        {
                            Name = "EnumTag",
                            TagType = TagType.Enumerated,
                            EnumType = null,
                            Categories = ImmutableList.Create(new TagCategory { Name = "V1", Lacv = 1 })
                        }
                    )
                }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Enumerated tag") && e.Contains("enumType"));
    }

    [Fact]
    public void Validate_CategoryNotBeforeAfterNotAfter_Error()
    {
        var spif = MakeValidSpif() with
        {
            CategoryTagSets = ImmutableList.Create(
                new SecurityCategoryTagSet
                {
                    TagSetOid = "2.16.840.1.1",
                    Name = "Test",
                    Tags = ImmutableList.Create(
                        new SecurityCategoryTag
                        {
                            Name = "T1",
                            TagType = TagType.Permissive,
                            Categories = ImmutableList.Create(
                                new TagCategory
                                {
                                    Name = "C1",
                                    Lacv = 1,
                                    NotBefore = DateTimeOffset.UtcNow.AddDays(10),
                                    NotAfter = DateTimeOffset.UtcNow.AddDays(-10)
                                }
                            )
                        }
                    )
                }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("notBefore") && e.Contains("notAfter"));
    }

    [Fact]
    public void Validate_SelfExcludingCategory_Error()
    {
        var spif = MakeValidSpif() with
        {
            CategoryTagSets = ImmutableList.Create(
                new SecurityCategoryTagSet
                {
                    TagSetOid = "2.16.840.1.101.2.1.8.3.1",
                    Name = "Test",
                    Tags = ImmutableList.Create(
                        new SecurityCategoryTag
                        {
                            Name = "T1",
                            TagType = TagType.Permissive,
                            Categories = ImmutableList.Create(
                                new TagCategory
                                {
                                    Name = "SelfExcluder",
                                    Lacv = 1,
                                    ExcludedCategories = ImmutableList.Create(
                                        new ExcludedCategoryRef { TagSetRef = "2.16.840.1.101.2.1.8.3.1", TagType = TagType.Permissive, Lacv = 1 }
                                    )
                                }
                            )
                        }
                    )
                }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("excludes itself"));
    }

    [Fact]
    public void Validate_EquivalentPolicySameOidAsSpif_Error()
    {
        var spif = MakeValidSpif() with
        {
            EquivalentPolicies = ImmutableList.Create(
                new EquivalentPolicy { Name = "Self", PolicyOid = "2.16.840.1.101.2.1.3.13.1" }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("same OID as the SPIF policy itself"));
    }

    [Fact]
    public void Validate_PrivacyMarksMinExceedsMax_Error()
    {
        var spif = MakeValidSpif() with
        {
            PrivacyMarks = new PrivacyMarks
            {
                MinSelection = 5,
                MaxSelection = 2,
                Marks = ImmutableList.Create(
                    new PrivacyMark { Name = "PM1" },
                    new PrivacyMark { Name = "PM2" }
                )
            }
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("minSelection cannot exceed maxSelection"));
    }

    [Fact]
    public void Validate_PrivacyMarksMinExceedsAvailable_Error()
    {
        var spif = MakeValidSpif() with
        {
            PrivacyMarks = new PrivacyMarks
            {
                MinSelection = 5,
                Marks = ImmutableList.Create(new PrivacyMark { Name = "PM1" })
            }
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds available marks"));
    }

    [Fact]
    public void Validate_NegativeLacv_Error()
    {
        var spif = MakeValidSpif() with
        {
            Classifications = ImmutableList.Create(
                new SecurityClassification { Name = "BAD", Lacv = -1, Hierarchy = 0 }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("negative LACV"));
    }

    [Fact]
    public void Validate_NoBaselineClassification_Warning()
    {
        var spif = MakeValidSpif() with
        {
            Classifications = ImmutableList.Create(
                new SecurityClassification { Name = "TOP SECRET", Lacv = 3, Hierarchy = 3, MarkingData = ImmutableList.Create(new MarkingData { Phrase = "TS", Codes = ImmutableList.Create("pageTop") }) }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.Warnings, w => w.Contains("baseline classification"));
    }

    [Fact]
    public void Validate_NonStandardSchemaVersion_Warning()
    {
        var spif = MakeValidSpif() with { SchemaVersion = "4.0" };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.Warnings, w => w.Contains("non-standard"));
    }

    [Fact]
    public void Validate_V30WithOldColorStyle_Warning()
    {
        var spif = MakeValidSpif() with
        {
            SchemaVersion = "3.0",
            Classifications = ImmutableList.Create(
                new SecurityClassification { Name = "UNCLASSIFIED", Lacv = 0, Hierarchy = 0, Color = "#00FF00", MarkingData = ImmutableList.Create(new MarkingData { Phrase = "U", Codes = ImmutableList.Create("pageTop") }) }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.Warnings, w => w.Contains("v2.1-style"));
    }

    [Fact]
    public void Validate_RestrictiveTagNonPowerOf2Lacv_Warning()
    {
        var spif = MakeValidSpif() with
        {
            CategoryTagSets = ImmutableList.Create(
                new SecurityCategoryTagSet
                {
                    TagSetOid = "2.16.840.1.1",
                    Name = "Test",
                    Tags = ImmutableList.Create(
                        new SecurityCategoryTag
                        {
                            Name = "Restrictive",
                            TagType = TagType.Restrictive,
                            Categories = ImmutableList.Create(
                                new TagCategory { Name = "A", Lacv = 1 },
                                new TagCategory { Name = "B", Lacv = 3 } // Not power of 2
                            )
                        }
                    )
                }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.Warnings, w => w.Contains("non-power-of-2"));
    }

    [Fact]
    public void Validate_EquivalentPolicyNoClassMapping_Warning()
    {
        var spif = MakeValidSpif() with
        {
            EquivalentPolicies = ImmutableList.Create(
                new EquivalentPolicy { Name = "OtherPolicy", PolicyOid = "1.2.3.4.5" }
            )
        };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.Warnings, w => w.Contains("no classification mappings"));
    }

    [Fact]
    public void Validate_NullSpif_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SpifValidator.Validate(null!));
    }

    [Fact]
    public void Validate_AllIssues_ContainsPrefixes()
    {
        var spif = MakeValidSpif() with
        {
            PolicyId = new PolicyInfo { Name = "Test", Oid = "bad-oid" }
        };

        var result = SpifValidator.Validate(spif);
        Assert.Contains(result.AllIssues, i => i.StartsWith("ERROR:"));
    }
}

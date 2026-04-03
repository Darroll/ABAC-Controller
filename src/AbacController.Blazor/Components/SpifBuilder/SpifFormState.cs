namespace AbacController.Blazor.Components.SpifBuilder;

/// <summary>
/// Mutable form state container for the SPIF form builder.
/// Passed as a CascadingParameter to child section components.
/// </summary>
public sealed class SpifFormState
{
    // ── Metadata ──
    public string PolicyOid { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public string? DefaultSecurityPolicyId { get; set; }

    // ── Classifications ──
    public List<ClassificationEntry> Classifications { get; set; } = [new("UNCLASSIFIED", 0)];

    // ── Tag Sets ──
    public List<TagSetEntry> TagSets { get; set; } = [];

    // ── Equivalent Policies ──
    public List<EquivalentPolicyEntry> EquivalentPolicies { get; set; } = [];

    // ── Extensions ──
    public string ExtensionMode { get; set; } = "fields";
    public List<ExtensionFieldEntry> ExtensionFields { get; set; } = [];
    public string RawExtensionXml { get; set; } = string.Empty;
    public string? ExtensionXmlError { get; set; }

    /// <summary>Resets all state to defaults for a fresh create/edit session.</summary>
    public void Reset()
    {
        PolicyOid = string.Empty;
        PolicyName = string.Empty;
        DefaultSecurityPolicyId = null;

        Classifications = [new("UNCLASSIFIED", 0)];
        TagSets = [];
        EquivalentPolicies = [];

        ExtensionMode = "fields";
        ExtensionFields = [];
        RawExtensionXml = string.Empty;
        ExtensionXmlError = null;
    }
}

// ═══════════════════════════════════════════════════════════════════
// Mutable form entry types for two-way binding
// ═══════════════════════════════════════════════════════════════════

/// <summary>Mutable classification entry for form binding.</summary>
public sealed record ClassificationEntry(
    string Name,
    int LacvValue,
    string? PhraseText = null,
    string? PortionAbbreviation = null,
    bool IsPortionMarking = true,
    string? MarkingCode = null,
    string? ForegroundColor = null,
    string? BackgroundColor = null);

/// <summary>Mutable tag set entry for form binding.</summary>
public sealed class TagSetEntry
{
    public string Name { get; set; } = string.Empty;
    public string? TagSetId { get; set; }
    public string? DisplayName { get; set; }
    public int DisplayOrder { get; set; }
    public int? MinLacv { get; set; }
    public bool Expanded { get; set; } = true;
    public List<TagEntry> Tags { get; set; } = [];
    public List<ConstraintEntry> Constraints { get; set; } = [];

    /// <summary>Optional user override for semantic UI grouping. Auto-detected when null.</summary>
    public string? SemanticCategory { get; set; }
}

/// <summary>Mutable tag entry for form binding.</summary>
public sealed record TagEntry
{
    public string TagName { get; init; } = string.Empty;
    public int LacvValue { get; init; } = 1;
    public string TagType { get; init; } = "restrictive";
    public string? PhraseText { get; init; }
    public string? PortionAbbreviation { get; init; }
    public bool IsPortionMarking { get; init; } = true;
    public string? MarkingCode { get; init; }
}

/// <summary>Mutable constraint entry for form binding.</summary>
public sealed record ConstraintEntry
{
    public string ConstraintType { get; init; } = "mutualExclusion";
    public string? SourceTagSetId { get; init; }
    public int SourceTagLacv { get; init; }
    public string? TargetTagSetId { get; init; }
    public int TargetTagLacv { get; init; }
    public string Severity { get; init; } = "error";
    public string? ErrorMessage { get; init; }
}

/// <summary>Mutable extension field entry for form binding.</summary>
public sealed record ExtensionFieldEntry
{
    public string FieldName { get; init; } = string.Empty;
    public string FieldValue { get; init; } = string.Empty;
    public string? CharSetConstraint { get; init; }
}

/// <summary>Mutable equivalent policy entry for form binding.</summary>
public sealed class EquivalentPolicyEntry
{
    public string PolicyOid { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public bool Expanded { get; set; } = true;
    public List<ClassificationMappingEntry> ClassificationMappings { get; set; } = [];
    public List<CategoryMappingEntry> CategoryMappings { get; set; } = [];
}

/// <summary>Mutable classification mapping entry for form binding.</summary>
public sealed class ClassificationMappingEntry
{
    public int SourceLacv { get; set; }
    public int TargetLacv { get; set; }
}

/// <summary>Mutable category mapping entry for form binding.</summary>
public sealed class CategoryMappingEntry
{
    public string SourceTagSetId { get; set; } = string.Empty;
    public int SourceLacv { get; set; }
    public string TargetTagSetId { get; set; } = string.Empty;
    public int TargetLacv { get; set; }
}

/// <summary>Helper methods for tag set semantic categorization.</summary>
public static class TagSetCategoryHelper
{
    public static string GetCategory(TagSetEntry tagSet)
    {
        if (!string.IsNullOrWhiteSpace(tagSet.SemanticCategory))
            return tagSet.SemanticCategory;

        if (tagSet.Tags.Count == 0) return "generic";

        var types = tagSet.Tags.Select(t => t.TagType).Distinct().ToList();
        if (types.Contains("restrictive") && types.Contains("permissive"))
            return "dissemination";

        var dominant = tagSet.Tags
            .GroupBy(t => t.TagType)
            .OrderByDescending(g => g.Count())
            .First().Key;

        return dominant switch
        {
            "permissive" => "releasability",
            "informative" => "informative",
            _ => "restrictive"
        };
    }

    public static string CategoryDisplayName(string category) => category switch
    {
        "restrictive" => "Access Controls",
        "dissemination" => "Dissemination Controls",
        "releasability" => "Releasability",
        "informative" => "Information Markers",
        _ => "Uncategorized"
    };

    public static string CategoryBorderColor(string category) => category switch
    {
        "restrictive" => "#dc3545",
        "dissemination" => "#fd7e14",
        "releasability" => "#198754",
        "informative" => "#0d6efd",
        _ => "#6c757d"
    };
}

namespace AbacController.Blazor.Components.SpifBuilder;

/// <summary>
/// Mutable form state container for the SPIF form builder.
/// Passed as a CascadingParameter to child section components.
/// </summary>
public sealed class SpifFormState
{
    // ── Metadata ──
    /// <summary>Gets or sets the policy Oid.</summary>
    public string PolicyOid { get; set; } = string.Empty;
    /// <summary>Gets or sets the policy Name.</summary>
    public string PolicyName { get; set; } = string.Empty;
    /// <summary>Gets or sets the default Security Policy Id.</summary>
    public string? DefaultSecurityPolicyId { get; set; }

    // ── Classifications ──
    public List<ClassificationEntry> Classifications { get; set; } = [new("UNCLASSIFIED", 0)];

    // ── Tag Sets ──
    /// <summary>Gets or sets the tag Sets.</summary>
    public List<TagSetEntry> TagSets { get; set; } = [];

    // ── Equivalent Policies ──
    /// <summary>Gets or sets the equivalent Policies.</summary>
    public List<EquivalentPolicyEntry> EquivalentPolicies { get; set; } = [];

    // ── Extensions ──
    /// <summary>Gets or sets the extension Mode.</summary>
    public string ExtensionMode { get; set; } = "fields";
    /// <summary>Gets or sets the extension Fields.</summary>
    public List<ExtensionFieldEntry> ExtensionFields { get; set; } = [];
    /// <summary>Gets or sets the raw Extension Xml.</summary>
    public string RawExtensionXml { get; set; } = string.Empty;
    /// <summary>Gets or sets the extension Xml Error.</summary>
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
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the tag Set Id.</summary>
    public string? TagSetId { get; set; }
    /// <summary>Gets or sets the display Name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Gets or sets the display Order.</summary>
    public int DisplayOrder { get; set; }
    /// <summary>Gets or sets the min Lacv.</summary>
    public int? MinLacv { get; set; }
    /// <summary>Gets or sets the expanded.</summary>
    public bool Expanded { get; set; } = true;
    /// <summary>Gets or sets the tags.</summary>
    public List<TagEntry> Tags { get; set; } = [];
    /// <summary>Gets or sets the constraints.</summary>
    public List<ConstraintEntry> Constraints { get; set; } = [];

    /// <summary>Optional user override for semantic UI grouping. Auto-detected when null.</summary>
    public string? SemanticCategory { get; set; }
}

/// <summary>Mutable tag entry for form binding.</summary>
public sealed record TagEntry
{
    /// <summary>Gets or sets the tag Name.</summary>
    public string TagName { get; init; } = string.Empty;
    /// <summary>Gets or sets the lacv Value.</summary>
    public int LacvValue { get; init; } = 1;
    /// <summary>Gets or sets the tag Type.</summary>
    public string TagType { get; init; } = "restrictive";
    /// <summary>Gets or sets the phrase Text.</summary>
    public string? PhraseText { get; init; }
    /// <summary>Gets or sets the portion Abbreviation.</summary>
    public string? PortionAbbreviation { get; init; }
    /// <summary>Gets or sets the is Portion Marking.</summary>
    public bool IsPortionMarking { get; init; } = true;
    /// <summary>Gets or sets the marking Code.</summary>
    public string? MarkingCode { get; init; }
}

/// <summary>Mutable constraint entry for form binding.</summary>
public sealed record ConstraintEntry
{
    /// <summary>Gets or sets the constraint Type.</summary>
    public string ConstraintType { get; init; } = "mutualExclusion";
    /// <summary>Gets or sets the source Tag Set Id.</summary>
    public string? SourceTagSetId { get; init; }
    /// <summary>Gets or sets the source Tag Lacv.</summary>
    public int SourceTagLacv { get; init; }
    /// <summary>Gets or sets the target Tag Set Id.</summary>
    public string? TargetTagSetId { get; init; }
    /// <summary>Gets or sets the target Tag Lacv.</summary>
    public int TargetTagLacv { get; init; }
    /// <summary>Gets or sets the severity.</summary>
    public string Severity { get; init; } = "error";
    /// <summary>Gets or sets the error Message.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Mutable extension field entry for form binding.</summary>
public sealed record ExtensionFieldEntry
{
    /// <summary>Gets or sets the field Name.</summary>
    public string FieldName { get; init; } = string.Empty;
    /// <summary>Gets or sets the field Value.</summary>
    public string FieldValue { get; init; } = string.Empty;
    /// <summary>Gets or sets the char Set Constraint.</summary>
    public string? CharSetConstraint { get; init; }
}

/// <summary>Mutable equivalent policy entry for form binding.</summary>
public sealed class EquivalentPolicyEntry
{
    /// <summary>Gets or sets the policy Oid.</summary>
    public string PolicyOid { get; set; } = string.Empty;
    /// <summary>Gets or sets the policy Name.</summary>
    public string PolicyName { get; set; } = string.Empty;
    /// <summary>Gets or sets the expanded.</summary>
    public bool Expanded { get; set; } = true;
    /// <summary>Gets or sets the classification Mappings.</summary>
    public List<ClassificationMappingEntry> ClassificationMappings { get; set; } = [];
    /// <summary>Gets or sets the category Mappings.</summary>
    public List<CategoryMappingEntry> CategoryMappings { get; set; } = [];
}

/// <summary>Mutable classification mapping entry for form binding.</summary>
public sealed class ClassificationMappingEntry
{
    /// <summary>Gets or sets the source Lacv.</summary>
    public int SourceLacv { get; set; }
    /// <summary>Gets or sets the target Lacv.</summary>
    public int TargetLacv { get; set; }
}

/// <summary>Mutable category mapping entry for form binding.</summary>
public sealed class CategoryMappingEntry
{
    /// <summary>Gets or sets the source Tag Set Id.</summary>
    public string SourceTagSetId { get; set; } = string.Empty;
    /// <summary>Gets or sets the source Lacv.</summary>
    public int SourceLacv { get; set; }
    /// <summary>Gets or sets the target Tag Set Id.</summary>
    public string TargetTagSetId { get; set; } = string.Empty;
    /// <summary>Gets or sets the target Lacv.</summary>
    public int TargetLacv { get; set; }
}

/// <summary>Helper methods for tag set semantic categorization.</summary>
public static class TagSetCategoryHelper
{
    /// <summary>
    /// Executes get Category.
    /// </summary>
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

    /// <summary>
    /// Executes category Display Name.
    /// </summary>
    public static string CategoryDisplayName(string category) => category switch
    {
        "restrictive" => "Access Controls",
        "dissemination" => "Dissemination Controls",
        "releasability" => "Releasability",
        "informative" => "Information Markers",
        _ => "Uncategorized"
    };

    /// <summary>
    /// Executes category Border Color.
    /// </summary>
    public static string CategoryBorderColor(string category) => category switch
    {
        "restrictive" => "#dc3545",
        "dissemination" => "#fd7e14",
        "releasability" => "#198754",
        "informative" => "#0d6efd",
        _ => "#6c757d"
    };
}

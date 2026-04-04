using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using AbacController.Core.Domain.Spif;

namespace AbacController.Pap;

/// <summary>
/// Comprehensive SPIF validator performing stricter schema, semantic,
/// and cross-reference checks beyond initial parse-time validation.
/// Use after successful parsing to surface warnings and errors that
/// may indicate interoperability problems or policy authoring mistakes.
/// </summary>
public static class SpifValidator
{
    private static readonly Regex ValidHexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex ValidOid = new("^[0-2](\\.[0-9]+)+$", RegexOptions.Compiled);

    /// <summary>
    /// Perform comprehensive validation of a parsed SPIF.
    /// Returns a result with all errors and warnings found.
    /// </summary>
    public static SpifValidationResult Validate(Spif spif)
    {
        ArgumentNullException.ThrowIfNull(spif);

        var errors = new List<string>();
        var warnings = new List<string>();

        ValidatePolicyId(spif, errors, warnings);
        ValidateClassifications(spif, errors, warnings);
        ValidateTagSets(spif, errors, warnings);
        ValidateEquivalentPolicies(spif, errors, warnings);
        ValidatePrivacyMarks(spif, errors, warnings);
        ValidateMarkingData(spif, errors, warnings);
        ValidateMarkingQualifiers(spif, errors, warnings);
        ValidateCrossReferences(spif, errors, warnings);
        ValidateEquivalencyMappings(spif, errors, warnings);
        ValidateHierarchyConsistency(spif, errors, warnings);

        return new SpifValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void ValidatePolicyId(Spif spif, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(spif.PolicyId.Oid))
        {
            errors.Add("Policy OID is required");
        }
        else if (!ValidOid.IsMatch(spif.PolicyId.Oid))
        {
            errors.Add($"Policy OID '{spif.PolicyId.Oid}' is not a valid ASN.1 OID");
        }
        else if (spif.PolicyId.Oid.Length > 128)
        {
            warnings.Add($"Policy OID '{spif.PolicyId.Oid}' is unusually long ({spif.PolicyId.Oid.Length} chars)");
        }

        if (string.IsNullOrWhiteSpace(spif.PolicyId.Name))
        {
            errors.Add("Policy name is required");
        }
        else if (spif.PolicyId.Name.Length > 256)
        {
            warnings.Add($"Policy name is unusually long ({spif.PolicyId.Name.Length} chars)");
        }

        if (spif.PolicyId.MarkingData.Count == 0)
        {
            warnings.Add("Policy ID has no marking data — human-readable rendering may be incomplete");
        }

        if (spif.SchemaVersion is not ("2.1" or "3.0"))
        {
            warnings.Add($"Schema version '{spif.SchemaVersion}' is non-standard; only 2.1 and 3.0 are defined");
        }

        if (!string.IsNullOrWhiteSpace(spif.PrivilegeId) && !ValidOid.IsMatch(spif.PrivilegeId))
        {
            errors.Add($"privilegeId '{spif.PrivilegeId}' is not a valid OID");
        }

        if (!string.IsNullOrWhiteSpace(spif.RbacId) && !ValidOid.IsMatch(spif.RbacId))
        {
            errors.Add($"rbacId '{spif.RbacId}' is not a valid OID");
        }
    }

    private static void ValidateClassifications(Spif spif, List<string> errors, List<string> warnings)
    {
        if (spif.Classifications.IsEmpty)
        {
            errors.Add("SPIF must define at least one security classification");
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lacvs = new HashSet<int>();
        var hierarchies = new HashSet<int>();
        var allObsolete = true;

        foreach (var cls in spif.Classifications)
        {
            if (string.IsNullOrWhiteSpace(cls.Name))
            {
                errors.Add("Classification name cannot be empty");
                continue;
            }

            if (!names.Add(cls.Name))
            {
                errors.Add($"Duplicate classification name '{cls.Name}'");
            }

            if (!lacvs.Add(cls.Lacv.Value))
            {
                errors.Add($"Duplicate classification LACV {cls.Lacv} for '{cls.Name}'");
            }

            if (!hierarchies.Add(cls.Hierarchy))
            {
                warnings.Add($"Multiple classifications share hierarchy value {cls.Hierarchy}");
            }

            if (cls.Lacv.Value < 0)
            {
                errors.Add($"Classification '{cls.Name}' has negative LACV {cls.Lacv}");
            }

            if (cls.Hierarchy < 0)
            {
                warnings.Add($"Classification '{cls.Name}' has negative hierarchy {cls.Hierarchy}");
            }

            if (!cls.Obsolete)
                allObsolete = false;

            // Color validation
            ValidateColor(cls.Color, $"classification '{cls.Name}' color", warnings);
            ValidateColor(cls.FgColor, $"classification '{cls.Name}' fgcolor", warnings);
            ValidateColor(cls.BgColor, $"classification '{cls.Name}' bgcolor", warnings);

            // v3.0 should use fgcolor/bgcolor, not color
            if (spif.SchemaVersion == "3.0" && cls.Color is not null && cls.FgColor is null && cls.BgColor is null)
            {
                warnings.Add($"Classification '{cls.Name}' uses v2.1-style 'color' attribute in a v3.0 SPIF; prefer 'fgcolor'/'bgcolor'");
            }

            // Marking data completeness
            if (cls.MarkingData.IsEmpty)
            {
                warnings.Add($"Classification '{cls.Name}' has no marking data — display rendering will lack label text");
            }

            // Validate excludedClass references in requiredCategories
            foreach (var req in cls.RequiredCategories)
            {
                ValidateRequiredCategoryConstraint(req, $"classification '{cls.Name}'", spif, errors, warnings);
            }

            // Validate excluded categories
            foreach (var exc in cls.ExcludedCategories)
            {
                ValidateExcludedCategoryRef(exc, $"classification '{cls.Name}'", spif, errors, warnings);
            }
        }

        if (allObsolete)
        {
            warnings.Add("All classifications are marked obsolete — no usable classification exists");
        }

        // Check for UNMARKED/UNCLASSIFIED baseline
        var hasBaseline = spif.Classifications.Any(c =>
            c.Hierarchy == 0 ||
            c.Name.Contains("UNCLASSIFIED", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("UNMARKED", StringComparison.OrdinalIgnoreCase));
        if (!hasBaseline)
        {
            warnings.Add("No baseline classification (hierarchy 0, UNCLASSIFIED, or UNMARKED) found — this may cause interoperability issues");
        }
    }

    private static void ValidateTagSets(Spif spif, List<string> errors, List<string> warnings)
    {
        var tagSetOids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tagSet in spif.CategoryTagSets)
        {
            if (string.IsNullOrWhiteSpace(tagSet.TagSetOid))
            {
                errors.Add("Tag set OID is required");
                continue;
            }

            if (!ValidOid.IsMatch(tagSet.TagSetOid))
            {
                errors.Add($"Tag set OID '{tagSet.TagSetOid}' is not a valid ASN.1 OID");
            }

            if (!tagSetOids.Add(tagSet.TagSetOid))
            {
                errors.Add($"Duplicate tag set OID '{tagSet.TagSetOid}'");
            }

            if (tagSet.Tags.IsEmpty)
            {
                warnings.Add($"Tag set '{tagSet.Name}' ({tagSet.TagSetOid}) has no tags defined");
                continue;
            }

            var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tagSet.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Name))
                {
                    errors.Add($"Tag name is required in tag set '{tagSet.Name}'");
                    continue;
                }

                if (!tagNames.Add(tag.Name))
                {
                    errors.Add($"Duplicate tag name '{tag.Name}' in tag set '{tagSet.Name}'");
                }

                // Enumerated tags must have enumType
                if (tag.TagType == TagType.Enumerated && tag.EnumType is null)
                {
                    errors.Add($"Enumerated tag '{tag.Name}' in tag set '{tagSet.Name}' must declare enumType");
                }

                if (tag.TagType != TagType.Enumerated && tag.EnumType is not null)
                {
                    warnings.Add($"Tag '{tag.Name}' in tag set '{tagSet.Name}' declares enumType but is not an enumerated tag");
                }

                if (tag.Categories.IsEmpty)
                {
                    warnings.Add($"Tag '{tag.Name}' in tag set '{tagSet.Name}' has no categories");
                    continue;
                }

                var catLacvs = new HashSet<int>();
                var catNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cat in tag.Categories)
                {
                    ValidateCategory(cat, tag, tagSet, spif, catLacvs, catNames, errors, warnings);
                }

                // For restrictive tags, warn if LACV values don't form proper bitmask
                if (tag.TagType == TagType.Restrictive)
                {
                    ValidateRestrictiveBitmask(tag, tagSet, warnings);
                }
            }
        }
    }

    private static void ValidateCategory(
        TagCategory cat, SecurityCategoryTag tag, SecurityCategoryTagSet tagSet,
        Spif spif, HashSet<int> catLacvs, HashSet<string> catNames,
        List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(cat.Name))
        {
            errors.Add($"Category name is required in tag '{tag.Name}' of tag set '{tagSet.Name}'");
            return;
        }

        if (!catNames.Add(cat.Name))
        {
            errors.Add($"Duplicate category name '{cat.Name}' in tag '{tag.Name}'");
        }

        if (!catLacvs.Add(cat.Lacv.Value))
        {
            errors.Add($"Duplicate category LACV {cat.Lacv} in tag '{tag.Name}' of tag set '{tagSet.Name}'");
        }

        if (cat.Lacv.Value < 0)
        {
            errors.Add($"Category '{cat.Name}' has negative LACV {cat.Lacv}");
        }

        // Validity window check
        if (cat.NotBefore is not null && cat.NotAfter is not null && cat.NotBefore > cat.NotAfter)
        {
            errors.Add($"Category '{cat.Name}' has notBefore ({cat.NotBefore:O}) later than notAfter ({cat.NotAfter:O})");
        }

        // requiredClass cross-reference
        if (cat.RequiredClass is not null)
        {
            if (!spif.Classifications.Any(c => string.Equals(c.Name, cat.RequiredClass, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Category '{cat.Name}' references unknown requiredClass '{cat.RequiredClass}'");
            }
        }

        // excludedClass cross-reference
        foreach (var excluded in cat.ExcludedClasses)
        {
            if (!spif.Classifications.Any(c => string.Equals(c.Name, excluded, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"Category '{cat.Name}' excludes unknown classification '{excluded}'");
            }
        }

        // Self-exclusion check
        foreach (var req in cat.RequiredCategories)
        {
            ValidateRequiredCategoryConstraint(req, $"category '{cat.Name}'", spif, errors, warnings);
        }

        foreach (var exc in cat.ExcludedCategories)
        {
            ValidateExcludedCategoryRef(exc, $"category '{cat.Name}'", spif, errors, warnings);

            // Check if a category excludes itself
            if (string.Equals(exc.TagSetRef, tagSet.TagSetOid, StringComparison.Ordinal) && exc.Lacv == cat.Lacv)
            {
                errors.Add($"Category '{cat.Name}' excludes itself (tagSetRef={exc.TagSetRef}, lacv={exc.Lacv})");
            }
        }

        // Marking data
        if (cat.MarkingData.IsEmpty)
        {
            warnings.Add($"Category '{cat.Name}' in tag '{tag.Name}' has no marking data");
        }

        // Equivalent category cross-references
        foreach (var equiv in cat.EquivalentCategories)
        {
            if (!ValidOid.IsMatch(equiv.TagSetId))
            {
                errors.Add($"Equivalent category in '{cat.Name}' uses invalid tagSetId '{equiv.TagSetId}'");
            }

            // Check that the policyRef matches a known equivalent policy
            if (!spif.EquivalentPolicies.Any(ep =>
                string.Equals(ep.Name, equiv.PolicyRef, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ep.PolicyOid, equiv.PolicyRef, StringComparison.Ordinal)))
            {
                warnings.Add($"Equivalent category in '{cat.Name}' references unknown policy '{equiv.PolicyRef}'");
            }
        }
    }

    private static void ValidateRestrictiveBitmask(SecurityCategoryTag tag, SecurityCategoryTagSet tagSet, List<string> warnings)
    {
        // In restrictive (bit-set) tags, LACVs should ideally be powers of 2
        var nonPowerOf2 = tag.Categories
            .Where(c => c.Lacv.Value > 0 && (c.Lacv.Value & (c.Lacv.Value - 1)) != 0)
            .Select(c => c.Name)
            .ToList();

        if (nonPowerOf2.Count > 0)
        {
            warnings.Add($"Restrictive tag '{tag.Name}' in tag set '{tagSet.Name}' has categories with non-power-of-2 LACVs: {string.Join(", ", nonPowerOf2)}. This may cause unexpected bitmask behavior.");
        }
    }

    private static void ValidateEquivalentPolicies(Spif spif, List<string> errors, List<string> warnings)
    {
        var oids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ep in spif.EquivalentPolicies)
        {
            if (string.IsNullOrWhiteSpace(ep.PolicyOid))
            {
                errors.Add("Equivalent policy OID is required");
                continue;
            }

            if (!ValidOid.IsMatch(ep.PolicyOid))
            {
                errors.Add($"Equivalent policy OID '{ep.PolicyOid}' is not a valid OID");
            }

            if (!oids.Add(ep.PolicyOid))
            {
                errors.Add($"Duplicate equivalent policy OID '{ep.PolicyOid}'");
            }

            if (string.Equals(ep.PolicyOid, spif.PolicyId.Oid, StringComparison.Ordinal))
            {
                errors.Add($"Equivalent policy '{ep.Name}' has the same OID as the SPIF policy itself ({ep.PolicyOid})");
            }

            if (!names.Add(ep.Name))
            {
                warnings.Add($"Duplicate equivalent policy name '{ep.Name}'");
            }

            // Validate docRefURI if present
            if (ep.DocRefUri is not null)
            {
                if (!Uri.TryCreate(ep.DocRefUri, UriKind.Absolute, out _) &&
                    !Uri.TryCreate(ep.DocRefUri, UriKind.Relative, out _))
                {
                    warnings.Add($"Equivalent policy '{ep.Name}' has invalid docRefURI '{ep.DocRefUri}'");
                }
            }
        }
    }

    private static void ValidatePrivacyMarks(Spif spif, List<string> errors, List<string> warnings)
    {
        if (spif.PrivacyMarks is null)
            return;

        if (spif.PrivacyMarks.MinSelection is not null && spif.PrivacyMarks.MinSelection < 0)
        {
            errors.Add("Privacy marks minSelection cannot be negative");
        }

        if (spif.PrivacyMarks.MaxSelection is not null && spif.PrivacyMarks.MaxSelection < 0)
        {
            errors.Add("Privacy marks maxSelection cannot be negative");
        }

        if (spif.PrivacyMarks.MinSelection is not null &&
            spif.PrivacyMarks.MaxSelection is not null &&
            spif.PrivacyMarks.MinSelection > spif.PrivacyMarks.MaxSelection)
        {
            errors.Add("Privacy marks minSelection cannot exceed maxSelection");
        }

        if (spif.PrivacyMarks.MinSelection is not null &&
            spif.PrivacyMarks.MinSelection > spif.PrivacyMarks.Marks.Count)
        {
            errors.Add($"Privacy marks minSelection ({spif.PrivacyMarks.MinSelection}) exceeds available marks ({spif.PrivacyMarks.Marks.Count})");
        }

        var markNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allObsolete = spif.PrivacyMarks.Marks.Count > 0;
        foreach (var mark in spif.PrivacyMarks.Marks)
        {
            if (string.IsNullOrWhiteSpace(mark.Name))
            {
                errors.Add("Privacy mark name cannot be empty");
                continue;
            }

            if (!markNames.Add(mark.Name))
            {
                errors.Add($"Duplicate privacy mark name '{mark.Name}'");
            }

            if (!mark.Obsolete)
                allObsolete = false;
        }

        if (allObsolete && spif.PrivacyMarks.Marks.Count > 0)
        {
            warnings.Add("All privacy marks are marked obsolete");
        }
    }

    private static void ValidateMarkingData(Spif spif, List<string> errors, List<string> warnings)
    {
        foreach (var md in spif.GlobalMarkingData)
        {
            ValidateMarkingDataEntry(md, "global marking data", errors, warnings);
        }

        foreach (var cls in spif.Classifications)
        {
            foreach (var md in cls.MarkingData)
            {
                ValidateMarkingDataEntry(md, $"classification '{cls.Name}' marking data", errors, warnings);
            }
        }
    }

    private static void ValidateMarkingDataEntry(MarkingData md, string context, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(md.Phrase))
        {
            errors.Add($"{context}: marking data phrase is required");
        }

        if (md.Codes.IsEmpty)
        {
            warnings.Add($"{context}: marking data for phrase '{md.Phrase}' has no code entries");
        }

        // Check for unusual characters in phrases that might break rendering
        if (md.Phrase is not null && md.Phrase.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
        {
            warnings.Add($"{context}: marking data phrase '{md.Phrase}' contains control characters");
        }
    }

    private static void ValidateMarkingQualifiers(Spif spif, List<string> errors, List<string> warnings)
    {
        var markingCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mq in spif.GlobalMarkingQualifiers)
        {
            if (string.IsNullOrWhiteSpace(mq.MarkingCode))
            {
                errors.Add("Marking qualifier markingCode is required");
                continue;
            }

            if (!markingCodes.Add(mq.MarkingCode))
            {
                warnings.Add($"Duplicate marking qualifier for code '{mq.MarkingCode}'");
            }

            if (mq.Qualifiers.IsEmpty)
            {
                warnings.Add($"Marking qualifier '{mq.MarkingCode}' has no qualifier entries");
            }

            foreach (var q in mq.Qualifiers)
            {
                if (string.IsNullOrWhiteSpace(q.Text))
                {
                    warnings.Add($"Marking qualifier '{mq.MarkingCode}' has an entry with empty text");
                }
            }
        }
    }

    private static void ValidateCrossReferences(Spif spif, List<string> errors, List<string> warnings)
    {
        // Collect all tag set OIDs for reference validation
        var knownTagSetOids = spif.CategoryTagSets
            .Select(ts => ts.TagSetOid)
            .ToHashSet(StringComparer.Ordinal);

        // Validate all requiredCategory/excludedCategory references point to existing tag sets
        foreach (var cls in spif.Classifications)
        {
            foreach (var req in cls.RequiredCategories)
            {
                foreach (var group in req.CategoryGroups)
                {
                    if (!knownTagSetOids.Contains(group.TagSetRef))
                    {
                        warnings.Add($"Classification '{cls.Name}' requiredCategory references unknown tag set '{group.TagSetRef}'");
                    }
                    else
                    {
                        ValidateCategoryLacvExists(group.TagSetRef, group.Lacv, spif, $"classification '{cls.Name}' requiredCategory", warnings);
                    }
                }
            }
        }

        // Validate equivalent classifications reference known equivalent policies
        foreach (var cls in spif.Classifications)
        {
            foreach (var equiv in cls.EquivalentClassifications)
            {
                if (!spif.EquivalentPolicies.Any(ep =>
                    string.Equals(ep.Name, equiv.PolicyRef, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ep.PolicyOid, equiv.PolicyRef, StringComparison.Ordinal)))
                {
                    warnings.Add($"Classification '{cls.Name}' has equivalent classification referencing unknown policy '{equiv.PolicyRef}'");
                }
            }
        }
    }

    private static void ValidateEquivalencyMappings(Spif spif, List<string> errors, List<string> warnings)
    {
        // For each equivalent policy, check that at least one classification maps to it
        foreach (var ep in spif.EquivalentPolicies)
        {
            var hasClassMapping = spif.Classifications.Any(cls =>
                cls.EquivalentClassifications.Any(ec =>
                    string.Equals(ec.PolicyRef, ep.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ec.PolicyRef, ep.PolicyOid, StringComparison.Ordinal)));

            if (!hasClassMapping)
            {
                warnings.Add($"Equivalent policy '{ep.Name}' ({ep.PolicyOid}) has no classification mappings — cross-domain label translation will be incomplete");
            }
        }
    }

    private static void ValidateHierarchyConsistency(Spif spif, List<string> errors, List<string> warnings)
    {
        if (spif.Classifications.Count < 2)
            return;

        // Check that hierarchy ordering is consistent with LACV ordering
        var ordered = spif.Classifications.OrderBy(c => c.Hierarchy).ToList();
        var lacvOrdered = spif.Classifications.OrderBy(c => c.Lacv.Value).ToList();

        // Just warn if the orderings are misaligned (this is allowed but unusual)
        if (!ordered.Select(c => c.Name).SequenceEqual(lacvOrdered.Select(c => c.Name)))
        {
            warnings.Add("Classification hierarchy order does not match LACV order — this is allowed but may indicate a configuration issue");
        }
    }

    private static void ValidateRequiredCategoryConstraint(
        RequiredCategoryConstraint constraint, string owner,
        Spif spif, List<string> errors, List<string> warnings)
    {
        if (constraint.CategoryGroups.IsEmpty)
        {
            errors.Add($"{owner} has a requiredCategory with no categoryGroup entries");
        }

        var op = constraint.Operation.ToLowerInvariant();
        if (op is not ("all" or "oneormore" or "onlyone"))
        {
            warnings.Add($"{owner} uses non-standard requiredCategory operation '{constraint.Operation}'");
        }

        if (op == "onlyone" && constraint.CategoryGroups.Count < 2)
        {
            warnings.Add($"{owner} uses 'onlyOne' operation with fewer than 2 category groups — this is always satisfied");
        }
    }

    private static void ValidateExcludedCategoryRef(
        ExcludedCategoryRef excluded, string owner,
        Spif spif, List<string> errors, List<string> warnings)
    {
        var knownTagSetOids = spif.CategoryTagSets
            .Select(ts => ts.TagSetOid)
            .ToHashSet(StringComparer.Ordinal);

        if (!knownTagSetOids.Contains(excluded.TagSetRef))
        {
            warnings.Add($"{owner} excludedCategory references unknown tag set '{excluded.TagSetRef}'");
        }
        else
        {
            ValidateCategoryLacvExists(excluded.TagSetRef, excluded.Lacv, spif, $"{owner} excludedCategory", warnings);
        }
    }

    private static void ValidateCategoryLacvExists(string tagSetOid, LacvValue lacv, Spif spif, string context, List<string> warnings)
    {
        var tagSet = spif.CategoryTagSets.FirstOrDefault(ts =>
            string.Equals(ts.TagSetOid, tagSetOid, StringComparison.Ordinal));
        if (tagSet is null) return;

        var exists = tagSet.Tags.SelectMany(t => t.Categories).Any(c => c.Lacv == lacv);
        if (!exists)
        {
            warnings.Add($"{context} references unknown category LACV {lacv} in tag set '{tagSetOid}'");
        }
    }

    private static void ValidateColor(string? color, string context, List<string> warnings)
    {
        if (color is null) return;

        if (!ValidHexColor.IsMatch(color) &&
            !color.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{context} has non-standard color value '{color}' — expected #RRGGBB format");
        }
    }
}

/// <summary>
/// Result of comprehensive SPIF validation.
/// </summary>
public sealed record SpifValidationResult(bool IsValid, List<string> Errors, List<string> Warnings)
{
    /// <summary>All issues (errors + warnings) for display.</summary>
    public IEnumerable<string> AllIssues => Errors.Select(e => $"ERROR: {e}").Concat(Warnings.Select(w => $"WARNING: {w}"));
}

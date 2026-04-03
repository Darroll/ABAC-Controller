using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// Core ACDF (Access Control Decision Function) evaluator.
/// Pure function: takes a label, clearance, and compiled SPIF index, returns PASS/FAIL.
/// No side effects, no I/O, no caching. This is the hot path.
/// </summary>
public sealed class AcdfEvaluator : IAcdfEvaluator
{
    /// <inheritdoc />
    public AcdfResult Evaluate(
        in SecurityLabel label,
        in SecurityClearance clearance,
        ISpifIndex spifIndex)
    {
        return EvaluateCore(in label, in clearance, spifIndex, trace: null);
    }

    /// <inheritdoc />
    public AcdfResult EvaluateWithTrace(
        in SecurityLabel label,
        in SecurityClearance clearance,
        ISpifIndex spifIndex,
        AcdfTraceCollector trace)
    {
        return EvaluateCore(in label, in clearance, spifIndex, trace);
    }

    private static AcdfResult EvaluateCore(
        in SecurityLabel label,
        in SecurityClearance clearance,
        ISpifIndex spifIndex,
        AcdfTraceCollector? trace)
    {
        var labelPolicyOid = string.IsNullOrWhiteSpace(label.PolicyOid)
            ? spifIndex.PolicyOid
            : label.PolicyOid;

        if (!PoliciesCompatible(labelPolicyOid!, clearance.PolicyOid, spifIndex, out var policyMessage))
        {
            trace?.AddStep("policy-match", "FAIL", false, policyMessage);
            return AcdfResult.Fail(AcdfFailureReason.PolicyMismatch, policyMessage);
        }

        trace?.AddStep("policy-match", "PASS", true, policyMessage);

        var validationResult = ValidateLabel(label, spifIndex, trace);
        if (!validationResult.Pass)
        {
            return validationResult;
        }

        if (!spifIndex.TryGetHierarchy(label.ClassificationLacv, out var labelHierarchy))
        {
            const string reason = "Label classification was not found in the governing SPIF";
            trace?.AddStep("classification-check", "FAIL", false, reason);
            return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
        }

        var maxClearanceHierarchy = int.MinValue;
        var foundClearanceClassification = false;

        foreach (var clearanceLacv in clearance.ClassificationLacvs)
        {
            if (!spifIndex.TryGetHierarchy(clearanceLacv, out var hierarchy))
            {
                continue;
            }

            foundClearanceClassification = true;
            if (hierarchy > maxClearanceHierarchy)
            {
                maxClearanceHierarchy = hierarchy;
            }
        }

        if (!foundClearanceClassification)
        {
            const string reason = "Clearance does not contain any classifications known to the governing SPIF";
            trace?.AddStep("classification-dominance", "FAIL", false, reason);
            return AcdfResult.Fail(AcdfFailureReason.ClassificationDominanceFailed, reason);
        }

        if (maxClearanceHierarchy < labelHierarchy)
        {
            var reason = $"Clearance max hierarchy {maxClearanceHierarchy} < label hierarchy {labelHierarchy}";
            trace?.AddStep("classification-dominance", "FAIL", false, reason);
            return AcdfResult.Fail(AcdfFailureReason.ClassificationDominanceFailed, reason);
        }

        trace?.AddStep(
            "classification-dominance",
            "PASS",
            true,
            $"Clearance hierarchy {maxClearanceHierarchy} >= label hierarchy {labelHierarchy}");

        foreach (var labelTagSet in label.CategoryTagSets)
        {
            var spifTagSet = spifIndex.GetTagSet(labelTagSet.TagSetOid);
            if (spifTagSet is null)
            {
                var reason = $"Label tag set {labelTagSet.TagSetOid} not found in SPIF";
                trace?.AddStep("category-tag-set", "FAIL", false, reason);
                return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
            }

            var clearanceTagSet = clearance.GetTagSet(labelTagSet.TagSetOid);

            foreach (var labelTag in labelTagSet.Tags)
            {
                var spifTag = ResolveTagDefinition(spifTagSet, labelTag);
                if (spifTag is null)
                {
                    var reason = $"Label tag {DescribeTag(labelTag)} not found in SPIF tag set {spifTagSet.Name}";
                    trace?.AddStep("category-definition", "FAIL", false, reason);
                    return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
                }

                var result = CheckCategoryTag(labelTag, spifTag, clearanceTagSet, trace);
                if (!result.Pass)
                {
                    return result;
                }
            }
        }

        trace?.AddStep("acdf-final", "PASS", true, "All ACDF checks passed");
        return AcdfResult.Passed();
    }

    private static bool PoliciesCompatible(
        string labelPolicyOid,
        string clearancePolicyOid,
        ISpifIndex spifIndex,
        out string message)
    {
        if (string.Equals(labelPolicyOid, clearancePolicyOid, StringComparison.Ordinal))
        {
            message = $"Policy OIDs match: {labelPolicyOid}";
            return true;
        }

        if (string.Equals(labelPolicyOid, spifIndex.PolicyOid, StringComparison.Ordinal) &&
            spifIndex.GetEquivalentPolicy(clearancePolicyOid) is not null)
        {
            message = $"Equivalent policy mapping found: {labelPolicyOid} → {clearancePolicyOid}";
            return true;
        }

        if (string.Equals(clearancePolicyOid, spifIndex.PolicyOid, StringComparison.Ordinal) &&
            spifIndex.GetEquivalentPolicy(labelPolicyOid) is not null)
        {
            message = $"Equivalent policy mapping found: {clearancePolicyOid} → {labelPolicyOid}";
            return true;
        }

        message = $"Label policy {labelPolicyOid} and clearance policy {clearancePolicyOid} are incompatible";
        return false;
    }

    private static AcdfResult ValidateLabel(
        in SecurityLabel label,
        ISpifIndex spifIndex,
        AcdfTraceCollector? trace)
    {
        var classification = spifIndex.GetClassification(label.ClassificationLacv);
        if (classification is null)
        {
            var reason = $"Classification lacv {label.ClassificationLacv} not found in SPIF";
            trace?.AddStep("label-validation", "FAIL", false, reason);
            return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
        }

        foreach (var tagSet in label.CategoryTagSets)
        {
            var spifTagSet = spifIndex.GetTagSet(tagSet.TagSetOid);
            if (spifTagSet is null)
            {
                var reason = $"Tag set {tagSet.TagSetOid} not found in SPIF";
                trace?.AddStep("label-validation", "FAIL", false, reason);
                return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
            }

            foreach (var labelTag in tagSet.Tags)
            {
                var spifTag = ResolveTagDefinition(spifTagSet, labelTag);
                if (spifTag is null)
                {
                    var reason = $"Tag {DescribeTag(labelTag)} not found in SPIF tag set {spifTagSet.Name}";
                    trace?.AddStep("label-validation", "FAIL", false, reason);
                    return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
                }

                foreach (var category in ResolveSelectedCategories(labelTag, spifTag))
                {
                    if (category.Definition is null)
                    {
                        var reason = $"Category lacv {category.Lacv} not found for tag {spifTag.Name}";
                        trace?.AddStep("label-validation", "FAIL", false, reason);
                        return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
                    }

                    var validityResult = ValidateCategoryValidity(category.Definition, category.Entry, trace);
                    if (!validityResult.Pass)
                    {
                        return validityResult;
                    }

                    if (!string.IsNullOrWhiteSpace(category.Definition.RequiredClass) &&
                        !MatchesClassificationRequirement(category.Definition.RequiredClass!, classification))
                    {
                        var reason =
                            $"Category {category.Definition.Name} requires classification {category.Definition.RequiredClass}";
                        trace?.AddStep("label-validation", "FAIL", false, reason);
                        return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
                    }

                    if (category.Definition.ExcludedClasses.Contains(classification.Name, StringComparer.Ordinal))
                    {
                        var reason =
                            $"Category {category.Definition.Name} excludes classification {classification.Name}";
                        trace?.AddStep("label-validation", "FAIL", false, reason);
                        return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
                    }

                    var categoryConstraintResult = ValidateRequiredCategories(
                        category.Definition.RequiredCategories,
                        label,
                        spifIndex,
                        $"category {category.Definition.Name}",
                        trace);
                    if (!categoryConstraintResult.Pass)
                    {
                        return categoryConstraintResult;
                    }

                    foreach (var excludedCategory in category.Definition.ExcludedCategories)
                    {
                        if (HasCategoryReference(label, excludedCategory, spifIndex))
                        {
                            var reason =
                                $"Category {category.Definition.Name} excludes category {excludedCategory.Lacv} in {excludedCategory.TagSetRef}";
                            trace?.AddStep("label-validation", "FAIL", false, reason);
                            return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
                        }
                    }
                }
            }
        }

        var classificationRequiredResult = ValidateRequiredCategories(
            classification.RequiredCategories,
            label,
            spifIndex,
            $"classification {classification.Name}",
            trace);
        if (!classificationRequiredResult.Pass)
        {
            return classificationRequiredResult;
        }

        foreach (var excludedCategory in classification.ExcludedCategories)
        {
            if (HasCategoryReference(label, excludedCategory, spifIndex))
            {
                var reason =
                    $"Classification {classification.Name} excludes category {excludedCategory.Lacv} in {excludedCategory.TagSetRef}";
                trace?.AddStep("label-validation", "FAIL", false, reason);
                return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
            }
        }

        trace?.AddStep("label-validation", "PASS", true, "Label is valid against the governing SPIF");
        return AcdfResult.Passed();
    }

    private static AcdfResult ValidateCategoryValidity(
        TagCategory definition,
        LabelCategory? entry,
        AcdfTraceCollector? trace)
    {
        var now = DateTimeOffset.UtcNow;
        var notBefore = entry?.NotBefore ?? definition.NotBefore;
        var notAfter = entry?.NotAfter ?? definition.NotAfter;

        if (notBefore.HasValue && now < notBefore.Value)
        {
            var reason = $"Category {definition.Name} not yet valid (notBefore: {notBefore})";
            trace?.AddStep("validity-period", "FAIL", false, reason);
            return AcdfResult.Fail(AcdfFailureReason.ValidityPeriodExpired, reason);
        }

        if (notAfter.HasValue && now > notAfter.Value)
        {
            var reason = $"Category {definition.Name} expired (notAfter: {notAfter})";
            trace?.AddStep("validity-period", "FAIL", false, reason);
            return AcdfResult.Fail(AcdfFailureReason.ValidityPeriodExpired, reason);
        }

        return AcdfResult.Passed();
    }

    private static AcdfResult ValidateRequiredCategories(
        IReadOnlyList<RequiredCategoryConstraint> constraints,
        SecurityLabel label,
        ISpifIndex spifIndex,
        string scope,
        AcdfTraceCollector? trace)
    {
        foreach (var constraint in constraints)
        {
            var matches = constraint.CategoryGroups.Count(group => HasCategoryReference(label, group, spifIndex));
            var operation = constraint.Operation.Trim().ToLowerInvariant();
            var satisfied = operation switch
            {
                "oneormore" => matches > 0 || constraint.CategoryGroups.Count == 0,
                "onlyone" => matches == 1,
                "all" => matches == constraint.CategoryGroups.Count,
                _ => matches == constraint.CategoryGroups.Count
            };

            if (!satisfied)
            {
                var reason = $"Required categories for {scope} not satisfied ({constraint.Operation}; matches={matches})";
                trace?.AddStep("label-validation", "FAIL", false, reason);
                return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed, reason);
            }
        }

        return AcdfResult.Passed();
    }

    private static bool HasCategoryReference(
        SecurityLabel label,
        CategoryGroupRef categoryGroup,
        ISpifIndex spifIndex)
    {
        return label.CategoryTagSets.Any(tagSet =>
        {
            var spifTagSet = spifIndex.GetTagSet(tagSet.TagSetOid);
            if (spifTagSet is null || !TagSetMatches(categoryGroup.TagSetRef, tagSet.TagSetOid, spifTagSet.Name))
            {
                return false;
            }

            return tagSet.Tags.Any(labelTag =>
            {
                var spifTag = ResolveTagDefinition(spifTagSet, labelTag);
                if (spifTag is null || spifTag.TagType != categoryGroup.TagType)
                {
                    return false;
                }

                if (categoryGroup.TagType == TagType.Enumerated &&
                    categoryGroup.EnumType.HasValue &&
                    spifTag.EnumType != categoryGroup.EnumType)
                {
                    return false;
                }

                return ResolveSelectedCategories(labelTag, spifTag)
                    .Any(category => category.Lacv == categoryGroup.Lacv);
            });
        });
    }

    private static bool HasCategoryReference(
        SecurityLabel label,
        ExcludedCategoryRef excludedCategory,
        ISpifIndex spifIndex)
    {
        var group = new CategoryGroupRef
        {
            TagSetRef = excludedCategory.TagSetRef,
            TagType = excludedCategory.TagType,
            Lacv = excludedCategory.Lacv
        };

        return HasCategoryReference(label, group, spifIndex);
    }

    private static bool MatchesClassificationRequirement(string requiredClass, SecurityClassification classification)
        => string.Equals(requiredClass, classification.Name, StringComparison.Ordinal) ||
           string.Equals(requiredClass, classification.Lacv.ToString(), StringComparison.Ordinal);

    private static AcdfResult CheckCategoryTag(
        LabelCategoryTag labelTag,
        SecurityCategoryTag spifTag,
        ClearanceCategoryTagSet? clearanceTagSet,
        AcdfTraceCollector? trace)
    {
        var clearanceTag = ResolveClearanceTag(clearanceTagSet, labelTag, spifTag);

        switch (spifTag.TagType)
        {
            case TagType.Restrictive:
            {
                foreach (var bit in labelTag.Bits)
                {
                    if (clearanceTag is null || !clearanceTag.Bits.Contains(bit))
                    {
                        var reason =
                            $"Restrictive category {spifTag.Name}: label has bit {bit} not in clearance";
                        trace?.AddStep($"restrictive-{spifTag.Name}", "FAIL", false, reason);
                        return AcdfResult.Fail(AcdfFailureReason.RestrictiveCategoryFailed, reason);
                    }
                }

                trace?.AddStep($"restrictive-{spifTag.Name}", "PASS", true, "All restrictive bits present in clearance");
                break;
            }

            case TagType.Permissive:
            {
                var anyMatch = clearanceTag is not null && labelTag.Bits.Any(clearanceTag.Bits.Contains);
                if (!anyMatch)
                {
                    var reason = $"Permissive category {spifTag.Name}: no matching bits in clearance";
                    trace?.AddStep($"permissive-{spifTag.Name}", "FAIL", false, reason);
                    return AcdfResult.Fail(AcdfFailureReason.PermissiveCategoryFailed, reason);
                }

                trace?.AddStep($"permissive-{spifTag.Name}", "PASS", true, "At least one permissive bit matched");
                break;
            }

            case TagType.Enumerated:
            {
                var clearanceValues = clearanceTag?.EnumeratedValues;
                if (spifTag.EnumType == EnumType.Restrictive)
                {
                    foreach (var val in labelTag.EnumeratedValues)
                    {
                        if (clearanceValues is null || !clearanceValues.Contains(val))
                        {
                            var reason = $"Enum restrictive {spifTag.Name}: label value {val} not in clearance";
                            trace?.AddStep($"enum-restrictive-{spifTag.Name}", "FAIL", false, reason);
                            return AcdfResult.Fail(AcdfFailureReason.EnumeratedCategoryFailed, reason);
                        }
                    }

                    trace?.AddStep($"enum-restrictive-{spifTag.Name}", "PASS", true, "All enumerated values present in clearance");
                }
                else
                {
                    var anyOverlap = clearanceValues is not null && labelTag.EnumeratedValues.Any(clearanceValues.Contains);
                    if (!anyOverlap)
                    {
                        var reason = $"Enum permissive {spifTag.Name}: no overlap with clearance";
                        trace?.AddStep($"enum-permissive-{spifTag.Name}", "FAIL", false, reason);
                        return AcdfResult.Fail(AcdfFailureReason.EnumeratedCategoryFailed, reason);
                    }

                    trace?.AddStep($"enum-permissive-{spifTag.Name}", "PASS", true, "At least one enumerated value overlaps");
                }

                break;
            }

            case TagType.TagType7:
                trace?.AddStep($"tagtype7-{spifTag.Name}", "SKIP", true, "TagType7 (informative) — skipped");
                break;

            case TagType.NotApplicable:
                trace?.AddStep($"tag-na-{spifTag.Name}", "SKIP", true, "NotApplicable tag — skipped");
                break;
        }

        return AcdfResult.Passed();
    }

    private static SecurityCategoryTag? ResolveTagDefinition(ITagSetIndex tagSetIndex, LabelCategoryTag labelTag)
    {
        if (!string.IsNullOrWhiteSpace(labelTag.TagOid))
        {
            var byOid = tagSetIndex.Tags.FirstOrDefault(tag =>
                string.Equals(tag.Name, labelTag.TagOid, StringComparison.Ordinal));
            if (byOid is not null)
            {
                return byOid;
            }
        }

        if (!string.IsNullOrWhiteSpace(labelTag.Name))
        {
            var byName = tagSetIndex.Tags.FirstOrDefault(tag =>
                string.Equals(tag.Name, labelTag.Name, StringComparison.Ordinal));
            if (byName is not null)
            {
                return byName;
            }
        }

        var matchingType = tagSetIndex.Tags.Where(tag => tag.TagType == labelTag.TagType).ToList();
        if (labelTag.TagType == TagType.Enumerated)
        {
            matchingType = matchingType
                .Where(tag => tag.EnumType == labelTag.EnumType)
                .ToList();
        }

        return matchingType.Count == 1 ? matchingType[0] : null;
    }

    private static ClearanceCategoryTag? ResolveClearanceTag(
        ClearanceCategoryTagSet? clearanceTagSet,
        LabelCategoryTag labelTag,
        SecurityCategoryTag spifTag)
    {
        if (clearanceTagSet is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(labelTag.TagOid))
        {
            var exact = clearanceTagSet.Tags.FirstOrDefault(tag =>
                string.Equals(tag.TagOid, labelTag.TagOid, StringComparison.Ordinal));
            if (exact is not null)
            {
                return exact;
            }
        }

        var bySpifName = clearanceTagSet.Tags.FirstOrDefault(tag =>
            string.Equals(tag.TagOid, spifTag.Name, StringComparison.Ordinal));
        if (bySpifName is not null)
        {
            return bySpifName;
        }

        var matchingType = clearanceTagSet.Tags.Where(tag => tag.TagType == spifTag.TagType).ToList();
        return matchingType.Count == 1 ? matchingType[0] : null;
    }

    private static IReadOnlyList<ResolvedLabelCategory> ResolveSelectedCategories(
        LabelCategoryTag labelTag,
        SecurityCategoryTag spifTag)
    {
        if (labelTag.Categories.Count > 0)
        {
            return labelTag.Categories
                .Select(category => new ResolvedLabelCategory(
                    category.Lacv,
                    category,
                    spifTag.Categories.FirstOrDefault(def => def.Lacv == category.Lacv)))
                .ToList();
        }

        var selectedLacvs = labelTag.TagType == TagType.Enumerated
            ? labelTag.EnumeratedValues
            : labelTag.Bits;

        return selectedLacvs
            .Select(lacv => new ResolvedLabelCategory(
                lacv,
                Entry: null,
                Definition: spifTag.Categories.FirstOrDefault(def => def.Lacv == lacv)))
            .ToList();
    }

    private static bool TagSetMatches(string tagSetRef, string tagSetOid, string tagSetName)
        => string.Equals(tagSetRef, tagSetOid, StringComparison.Ordinal) ||
           string.Equals(tagSetRef, tagSetName, StringComparison.Ordinal);

    private static string DescribeTag(LabelCategoryTag tag)
        => tag.Name ?? tag.TagOid ?? tag.TagType.ToString();

    private readonly record struct ResolvedLabelCategory(
        LacvValue Lacv,
        LabelCategory? Entry,
        TagCategory? Definition);
}

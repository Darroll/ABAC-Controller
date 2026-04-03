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
        // Step 1: Policy OID match
        var labelPolicyOid = label.PolicyOid ?? spifIndex.PolicyOid;
        if (labelPolicyOid != clearance.PolicyOid)
        {
            var mapping = spifIndex.GetEquivalentPolicy(clearance.PolicyOid);
            if (mapping is null)
            {
                trace?.AddStep("policy-match", "FAIL", false,
                    $"Label policy {labelPolicyOid} != clearance policy {clearance.PolicyOid}, no equivalency mapping");
                return AcdfResult.Fail(AcdfFailureReason.PolicyMismatch,
                    $"Label policy {labelPolicyOid} != clearance policy {clearance.PolicyOid}, no equivalency mapping");
            }
            trace?.AddStep("policy-match", "PASS", true,
                $"Equivalent policy mapping found: {labelPolicyOid} → {clearance.PolicyOid}");
        }
        else
        {
            trace?.AddStep("policy-match", "PASS", true,
                $"Policy OIDs match: {labelPolicyOid}");
        }

        // Step 2: Classification hierarchy dominance
        if (!spifIndex.TryGetHierarchy(label.ClassificationLacv, out int labelHierarchy))
        {
            trace?.AddStep("classification-check", "FAIL", false,
                $"Classification lacv {label.ClassificationLacv} not found in SPIF");
            return AcdfResult.Fail(AcdfFailureReason.LabelValidationFailed,
                $"Classification lacv {label.ClassificationLacv} not found in SPIF");
        }

        int maxClearanceHierarchy = 0;
        foreach (var clearanceLacv in clearance.ClassificationLacvs)
        {
            if (spifIndex.TryGetHierarchy(clearanceLacv, out int h))
                maxClearanceHierarchy = Math.Max(maxClearanceHierarchy, h);
        }

        if (maxClearanceHierarchy < labelHierarchy)
        {
            trace?.AddStep("classification-dominance", "FAIL", false,
                $"Clearance max hierarchy {maxClearanceHierarchy} < label hierarchy {labelHierarchy}");
            return AcdfResult.Fail(AcdfFailureReason.ClassificationDominanceFailed,
                $"Clearance max hierarchy {maxClearanceHierarchy} < label hierarchy {labelHierarchy}");
        }
        trace?.AddStep("classification-dominance", "PASS", true,
            $"Clearance hierarchy {maxClearanceHierarchy} >= label hierarchy {labelHierarchy}");

        // Step 3: Category checks (per tag set)
        foreach (var labelTagSet in label.CategoryTagSets)
        {
            // Find matching clearance tag set
            var clearanceTagSet = clearance.CategoryTagSets
                .FirstOrDefault(ts => ts.TagSetOid == labelTagSet.TagSetOid);

            foreach (var labelTag in labelTagSet.Tags)
            {
                var result = CheckCategoryTag(labelTag, clearanceTagSet, trace);
                if (!result.Pass)
                    return result;
            }
        }

        // Step 4: Validity period check
        var now = DateTimeOffset.UtcNow;
        foreach (var tagSet in label.CategoryTagSets)
        {
            foreach (var tag in tagSet.Tags)
            {
                foreach (var cat in tag.Categories)
                {
                    if (cat.NotBefore.HasValue && now < cat.NotBefore.Value)
                    {
                        trace?.AddStep("validity-period", "FAIL", false,
                            $"Category {cat.Name} not yet valid (notBefore: {cat.NotBefore})");
                        return AcdfResult.Fail(AcdfFailureReason.ValidityPeriodExpired,
                            $"Category {cat.Name} not yet valid (notBefore: {cat.NotBefore})");
                    }
                    if (cat.NotAfter.HasValue && now > cat.NotAfter.Value)
                    {
                        trace?.AddStep("validity-period", "FAIL", false,
                            $"Category {cat.Name} expired (notAfter: {cat.NotAfter})");
                        return AcdfResult.Fail(AcdfFailureReason.ValidityPeriodExpired,
                            $"Category {cat.Name} expired (notAfter: {cat.NotAfter})");
                    }
                }
            }
        }
        trace?.AddStep("validity-period", "PASS", true, "All categories within validity period");

        trace?.AddStep("acdf-final", "PASS", true, "All ACDF checks passed");
        return AcdfResult.Passed();
    }

    private static AcdfResult CheckCategoryTag(
        LabelCategoryTag labelTag,
        ClearanceCategoryTagSet? clearanceTagSet,
        AcdfTraceCollector? trace)
    {
        switch (labelTag.TagType)
        {
            case TagType.Restrictive:
            {
                // AND semantics: all label bits must be in clearance
                var clearanceBits = clearanceTagSet?.GetBits(labelTag.TagOid);
                foreach (var bit in labelTag.Bits)
                {
                    if (clearanceBits is null || !clearanceBits.Contains(bit))
                    {
                        trace?.AddStep($"restrictive-{labelTag.Name}", "FAIL", false,
                            $"Restrictive category {labelTag.Name}: label has bit {bit} not in clearance");
                        return AcdfResult.Fail(AcdfFailureReason.RestrictiveCategoryFailed,
                            $"Restrictive category {labelTag.Name}: label has bit {bit} not in clearance");
                    }
                }
                trace?.AddStep($"restrictive-{labelTag.Name}", "PASS", true,
                    $"All restrictive bits present in clearance");
                break;
            }

            case TagType.Permissive:
            {
                // OR semantics: at least one label bit must be in clearance
                var clearanceBits = clearanceTagSet?.GetBits(labelTag.TagOid);
                bool anyMatch = false;
                if (clearanceBits is not null)
                {
                    foreach (var bit in labelTag.Bits)
                    {
                        if (clearanceBits.Contains(bit))
                        {
                            anyMatch = true;
                            break;
                        }
                    }
                }
                if (!anyMatch)
                {
                    trace?.AddStep($"permissive-{labelTag.Name}", "FAIL", false,
                        $"Permissive category {labelTag.Name}: no matching bits in clearance");
                    return AcdfResult.Fail(AcdfFailureReason.PermissiveCategoryFailed,
                        $"Permissive category {labelTag.Name}: no matching bits in clearance");
                }
                trace?.AddStep($"permissive-{labelTag.Name}", "PASS", true,
                    $"At least one permissive bit matched");
                break;
            }

            case TagType.Enumerated:
            {
                var clearanceValues = clearanceTagSet?.GetEnumeratedValues(labelTag.TagOid);
                if (labelTag.EnumType == EnumType.Restrictive)
                {
                    // All label values must be in clearance
                    foreach (var val in labelTag.EnumeratedValues)
                    {
                        if (clearanceValues is null || !clearanceValues.Contains(val))
                        {
                            trace?.AddStep($"enum-restrictive-{labelTag.Name}", "FAIL", false,
                                $"Enum restrictive {labelTag.Name}: label value {val} not in clearance");
                            return AcdfResult.Fail(AcdfFailureReason.EnumeratedCategoryFailed,
                                $"Enum restrictive {labelTag.Name}: label value {val} not in clearance");
                        }
                    }
                    trace?.AddStep($"enum-restrictive-{labelTag.Name}", "PASS", true,
                        "All enumerated values present in clearance");
                }
                else
                {
                    // Permissive: at least one label value must be in clearance
                    bool anyOverlap = false;
                    if (clearanceValues is not null)
                    {
                        foreach (var val in labelTag.EnumeratedValues)
                        {
                            if (clearanceValues.Contains(val))
                            {
                                anyOverlap = true;
                                break;
                            }
                        }
                    }
                    if (!anyOverlap)
                    {
                        trace?.AddStep($"enum-permissive-{labelTag.Name}", "FAIL", false,
                            $"Enum permissive {labelTag.Name}: no overlap with clearance");
                        return AcdfResult.Fail(AcdfFailureReason.EnumeratedCategoryFailed,
                            $"Enum permissive {labelTag.Name}: no overlap with clearance");
                    }
                    trace?.AddStep($"enum-permissive-{labelTag.Name}", "PASS", true,
                        "At least one enumerated value overlaps");
                }
                break;
            }

            case TagType.TagType7:
                // Informative — NOT checked in ACDF
                trace?.AddStep($"tagtype7-{labelTag.Name}", "SKIP", true,
                    "TagType7 (informative) — skipped");
                break;

            case TagType.NotApplicable:
                break;
        }

        return AcdfResult.Passed();
    }
}

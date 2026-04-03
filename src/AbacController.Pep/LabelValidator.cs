using AbacController.Core.Domain.Labels;
using AbacController.Core.Interfaces;

namespace AbacController.Pep;

/// <summary>
/// Validates security labels against SPIF constraints.
/// </summary>
public sealed class LabelValidator
{
    /// <summary>
    /// Validate a security label against a SPIF.
    /// Checks classification existence, category existence, excludedClass,
    /// excludedCategory, and requiredCategory constraints.
    /// </summary>
    public LabelValidationResult Validate(SecurityLabel label, ISpifIndex spifIndex)
    {
        var errors = new List<string>();

        // Check classification exists
        var cls = spifIndex.GetClassification(label.ClassificationLacv);
        if (cls is null)
        {
            errors.Add($"Classification lacv {label.ClassificationLacv} not found in SPIF");
            return new LabelValidationResult(false, errors);
        }

        if (cls.Obsolete)
            errors.Add($"Classification '{cls.Name}' is marked obsolete");

        // Check all category tag set OIDs exist
        foreach (var tagSet in label.CategoryTagSets)
        {
            var spifTagSet = spifIndex.GetTagSet(tagSet.TagSetOid);
            if (spifTagSet is null)
            {
                errors.Add($"Tag set OID '{tagSet.TagSetOid}' not found in SPIF");
                continue;
            }

            // Check all category values exist
            foreach (var tag in tagSet.Tags)
            {
                foreach (var cat in tag.Categories)
                {
                    var spifCat = spifTagSet.GetCategory(tag.Name ?? "", cat.Lacv);
                    if (spifCat is null)
                    {
                        errors.Add($"Category lacv {cat.Lacv} (name: '{cat.Name}') " +
                            $"not found in tag '{tag.Name}' of tag set '{spifTagSet.Name}'");
                        continue;
                    }

                    if (spifCat.Obsolete)
                        errors.Add($"Category '{spifCat.Name}' is marked obsolete");

                    // Check excludedClass constraints
                    foreach (var excluded in spifCat.ExcludedClasses)
                    {
                        if (cls.Name == excluded)
                        {
                            errors.Add($"Category '{spifCat.Name}' excludes classification '{excluded}'");
                        }
                    }
                }
            }
        }

        // Check requiredCategory constraints on the classification
        foreach (var required in cls.RequiredCategories)
        {
            bool satisfied = CheckRequiredConstraint(required, label);
            if (!satisfied)
            {
                errors.Add($"Classification '{cls.Name}' requires category constraint " +
                    $"(operation: {required.Operation}) that is not satisfied");
            }
        }

        return new LabelValidationResult(errors.Count == 0, errors);
    }

    private static bool CheckRequiredConstraint(
        Core.Domain.Spif.RequiredCategoryConstraint constraint,
        SecurityLabel label)
    {
        var matches = constraint.CategoryGroups.Count(group => HasCategory(label, group.TagSetRef, group.Lacv));

        return constraint.Operation.ToLowerInvariant() switch
        {
            "oneormore" => matches > 0,
            "onlyone" => matches == 1,
            _ => matches == constraint.CategoryGroups.Count
        };
    }

    private static bool HasCategory(SecurityLabel label, string tagSetRef, Core.Domain.Spif.LacvValue lacv)
    {
        foreach (var tagSet in label.CategoryTagSets)
        {
            if (!string.Equals(tagSet.TagSetOid, tagSetRef, StringComparison.Ordinal) &&
                !string.Equals(tagSet.TagSetOid, tagSetRef, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var tag in tagSet.Tags)
            {
                if (tag.Bits.Contains(lacv) || tag.EnumeratedValues.Contains(lacv))
                    return true;
                foreach (var cat in tag.Categories)
                {
                    if (cat.Lacv == lacv)
                        return true;
                }
            }
        }
        return false;
    }
}

/// <summary>
/// Result of label validation.
/// </summary>
public sealed record LabelValidationResult(bool IsValid, List<string> Errors);

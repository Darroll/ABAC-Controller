using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Policy;

namespace AbacController.Pdp;

/// <summary>
/// Detects conflicts between policies by analyzing rule conditions and effects.
/// A conflict exists when two rules in the same policy set could match the same
/// request but produce different effects (Permit vs Deny).
/// </summary>
public static class PolicyConflictDetector
{
    /// <summary>
    /// Detect potential conflicts between a candidate policy and existing policies.
    /// </summary>
    public static List<PolicyConflict> DetectConflicts(
        string candidateContent,
        IReadOnlyList<PolicySet> existingPolicySets)
    {
        var conflicts = new List<PolicyConflict>();

        if (!TryParseRules(candidateContent, out var candidateRules))
            return conflicts;

        foreach (var policySet in existingPolicySets.Where(static ps => ps.IsActive))
        {
            foreach (var policy in policySet.Policies)
            {
                var version = policy.Versions
                    .Where(v => v.IsActive || policy.ActiveVersionId == v.Id)
                    .OrderByDescending(static v => v.VersionNumber)
                    .FirstOrDefault()
                    ?? policy.Versions.OrderByDescending(static v => v.VersionNumber).FirstOrDefault();

                if (version is null || !TryParseRules(version.Content, out var existingRules))
                    continue;

                foreach (var candidateRule in candidateRules)
                {
                    foreach (var existingRule in existingRules)
                    {
                        if (candidateRule.Effect != existingRule.Effect &&
                            ConditionsOverlap(candidateRule.Conditions, existingRule.Conditions))
                        {
                            conflicts.Add(new PolicyConflict
                            {
                                CandidateRuleId = candidateRule.Id,
                                CandidateEffect = candidateRule.Effect.ToString(),
                                ExistingPolicySetId = policySet.Id,
                                ExistingPolicyId = policy.Id,
                                ExistingRuleId = existingRule.Id,
                                ExistingEffect = existingRule.Effect.ToString(),
                                Reason = $"Rule '{candidateRule.Id}' ({candidateRule.Effect}) potentially conflicts with existing rule '{existingRule.Id}' ({existingRule.Effect}) in policy '{policy.Id}' — overlapping conditions detected"
                            });
                        }
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Check if two sets of conditions could potentially match the same request.
    /// Conservative analysis — reports potential conflicts (may have false positives).
    /// </summary>
    private static bool ConditionsOverlap(List<ParsedCondition> a, List<ParsedCondition> b)
    {
        // No conditions = matches everything → always overlaps
        if (a.Count == 0 || b.Count == 0)
            return true;

        // Check if any condition paths are the same with contradictory values
        foreach (var condA in a)
        {
            foreach (var condB in b)
            {
                if (string.IsNullOrWhiteSpace(condA.Path) || string.IsNullOrWhiteSpace(condB.Path))
                    continue;

                if (!string.Equals(condA.Path, condB.Path, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Same path — check if values are mutually exclusive
                if (condA.EqualsValue is not null && condB.EqualsValue is not null)
                {
                    var aStr = condA.EqualsValue.Value.ToString();
                    var bStr = condB.EqualsValue.Value.ToString();
                    if (!string.Equals(aStr, bStr, StringComparison.OrdinalIgnoreCase))
                    {
                        // Different values on same path — no overlap for this path
                        return false;
                    }
                }

                // Same path and same or unknown values — could overlap
                return true;
            }
        }

        // No shared paths — conditions are on different attributes, could overlap
        return true;
    }

    private static bool TryParseRules(string content, out List<ParsedRule> rules)
    {
        rules = [];
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("rules", out var rulesElement) && rulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var ruleElement in rulesElement.EnumerateArray())
                {
                    rules.Add(ParseRule(ruleElement));
                }
            }
            else
            {
                rules.Add(ParseRule(root));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ParsedRule ParseRule(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? "unknown"
            : "unknown";

        var effect = element.TryGetProperty("effect", out var effectEl) && effectEl.ValueKind == JsonValueKind.String
            && Enum.TryParse<Decision>(effectEl.GetString(), true, out var parsed)
                ? parsed
                : Decision.Deny;

        var conditions = new List<ParsedCondition>();
        if (element.TryGetProperty("conditions", out var conditionsEl) && conditionsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var condEl in conditionsEl.EnumerateArray())
            {
                conditions.Add(new ParsedCondition
                {
                    Path = condEl.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null,
                    EqualsValue = condEl.TryGetProperty("equals", out var equalsEl) ? equalsEl.Clone() : null
                });
            }
        }

        return new ParsedRule(id, effect, conditions);
    }

    private sealed record ParsedRule(string Id, Decision Effect, List<ParsedCondition> Conditions);

    private sealed class ParsedCondition
    {
        /// <summary>Gets or sets the path.</summary>
        public string? Path { get; init; }
        /// <summary>Gets or sets the equals Value.</summary>
        public JsonElement? EqualsValue { get; init; }
    }
}

/// <summary>
/// A detected conflict between policies.
/// </summary>
public sealed record PolicyConflict
{
    /// <summary>Gets or sets the candidate Rule Id.</summary>
    public required string CandidateRuleId { get; init; }
    /// <summary>Gets or sets the candidate Effect.</summary>
    public required string CandidateEffect { get; init; }
    /// <summary>Gets or sets the existing Policy Set Id.</summary>
    public required string ExistingPolicySetId { get; init; }
    /// <summary>Gets or sets the existing Policy Id.</summary>
    public required string ExistingPolicyId { get; init; }
    /// <summary>Gets or sets the existing Rule Id.</summary>
    public required string ExistingRuleId { get; init; }
    /// <summary>Gets or sets the existing Effect.</summary>
    public required string ExistingEffect { get; init; }
    /// <summary>Gets or sets the reason.</summary>
    public required string Reason { get; init; }
}

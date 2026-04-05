using System.Globalization;
using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Policy;

namespace AbacController.Pdp;

/// <summary>
/// Evaluates native JSON policy documents against evaluation requests.
/// Supports deny-overrides, permit-overrides, first-applicable, and only-one-applicable combining algorithms.
/// </summary>
internal static class NativePolicyEvaluator
{
    /// <summary>
    /// Computes a stable fingerprint for the active policy versions participating in evaluation.
    /// </summary>
    public static string ComputeVersionFingerprint(
        IReadOnlyList<PolicySet> policySets,
        string? requestedVersion)
    {
        var parts = new List<string>();

        foreach (var policySet in policySets.Where(static ps => ps.IsActive).OrderBy(static ps => ps.Id, StringComparer.Ordinal))
        {
            foreach (var policy in policySet.Policies.OrderBy(static p => p.Id, StringComparer.Ordinal))
            {
                var version = SelectVersion(policy, requestedVersion);
                if (version is null)
                {
                    continue;
                }

                parts.Add($"{policySet.Id}:{policy.Id}:{version.VersionNumber}:{version.Id:N}");
            }
        }

        return parts.Count == 0 ? "-" : string.Join(';', parts);
    }

    /// <summary>
    /// Collects subject attribute paths referenced by active policy conditions.
    /// </summary>
    public static IReadOnlySet<string> CollectRequiredSubjectAttributes(
        IReadOnlyList<PolicySet> policySets,
        string? requestedVersion)
    {
        var attributes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var policySet in policySets.Where(static ps => ps.IsActive))
        {
            foreach (var policy in policySet.Policies)
            {
                var version = SelectVersion(policy, requestedVersion);
                if (version is null)
                {
                    continue;
                }

                if (!TryParseDocument(version.Content, out var document, out _))
                {
                    continue;
                }

                foreach (var rule in document.Rules)
                {
                    CollectSubjectAttributes(rule.Conditions, attributes);
                }
            }
        }

        return attributes;
    }

    /// <summary>
    /// Evaluates the active policy sets against the supplied request.
    /// </summary>
    public static PolicyEvaluationOutcome Evaluate(
        EvaluationRequest request,
        IReadOnlyList<PolicySet> policySets,
        string? requestedVersion)
    {
        if (policySets.Count == 0)
        {
            return new PolicyEvaluationOutcome(
                Decision.NotApplicable,
                [],
                [],
                [],
                "No active policy sets available",
                []);
        }

        var setOutcomes = new List<NamedDecision>();
        var traceSteps = new List<TraceStep>();

        foreach (var policySet in policySets.Where(static ps => ps.IsActive))
        {
            var policyOutcomes = new List<NamedDecision>();

            foreach (var policy in policySet.Policies)
            {
                var version = SelectVersion(policy, requestedVersion);
                if (version is null)
                {
                    continue;
                }

                if (!TryParseDocument(version.Content, out var document, out var parseError))
                {
                    var policyReference = $"policy:{policy.Id}@v{version.VersionNumber}";
                    policyOutcomes.Add(new NamedDecision(
                        policyReference,
                        Decision.Indeterminate,
                        [policyReference],
                        [],
                        [],
                        $"Policy parse failed: {parseError}"));

                    traceSteps.Add(new TraceStep
                    {
                        RuleId = policyReference,
                        Effect = Decision.Indeterminate.ToString(),
                        Result = false,
                        Reason = $"Policy parse failed: {parseError}"
                    });
                    continue;
                }

                var policyDecision = EvaluatePolicy(request, policy, version, document, traceSteps);
                policyOutcomes.Add(policyDecision);
            }

            var combined = Combine(policySet.CombiningAlgorithm, policyOutcomes, $"policy-set:{policySet.Id}");
            if (combined.Decision != Decision.NotApplicable)
            {
                combined = combined with
                {
                    AppliedPolicies = combined.AppliedPolicies
                        .Append($"policy-set:{policySet.Id}")
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                };
            }

            setOutcomes.Add(combined);
        }

        var combinedOutcome = Combine("deny-overrides", setOutcomes, "policy-layer");
        return new PolicyEvaluationOutcome(
            combinedOutcome.Decision,
            combinedOutcome.AppliedPolicies,
            combinedOutcome.Obligations,
            combinedOutcome.Advice,
            combinedOutcome.Message,
            traceSteps);
    }

    private static NamedDecision EvaluatePolicy(
        EvaluationRequest request,
        Policy policy,
        PolicyVersion version,
        NativePolicyDocument document,
        List<TraceStep> traceSteps)
    {
        var policyReference = $"policy:{policy.Id}@v{version.VersionNumber}";
        var ruleOutcomes = new List<NamedDecision>();

        foreach (var rule in document.Rules)
        {
            var matched = rule.Conditions.Count == 0 || rule.Conditions.All(condition => EvaluateCondition(request, condition));
            var reason = matched
                ? $"Rule {rule.Id} matched"
                : $"Rule {rule.Id} did not match";

            traceSteps.Add(new TraceStep
            {
                RuleId = rule.Id,
                Effect = rule.Effect.ToString(),
                Result = matched,
                Reason = reason
            });

            if (!matched)
            {
                continue;
            }

            ruleOutcomes.Add(new NamedDecision(
                $"{policyReference}:rule:{rule.Id}",
                rule.Effect,
                [policyReference],
                rule.Obligations,
                rule.Advice,
                reason));
        }

        return Combine(document.CombiningAlgorithm, ruleOutcomes, policyReference);
    }

    private static NamedDecision Combine(string? algorithm, IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        var normalized = string.IsNullOrWhiteSpace(algorithm)
            ? "deny-overrides"
            : algorithm.Trim().ToLowerInvariant();

        return normalized switch
        {
            "permit-overrides" => CombinePermitOverrides(outcomes, scope),
            "first-applicable" => CombineFirstApplicable(outcomes, scope),
            "only-one-applicable" => CombineOnlyOneApplicable(outcomes, scope),
            "deny-unless-permit" => CombineDenyUnlessPermit(outcomes, scope),
            "permit-unless-deny" => CombinePermitUnlessDeny(outcomes, scope),
            _ => CombineDenyOverrides(outcomes, scope)
        };
    }

    private static NamedDecision CombineDenyOverrides(IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        if (outcomes.Count == 0)
        {
            return new NamedDecision(scope, Decision.NotApplicable, [], [], [], $"{scope} had no applicable policies");
        }

        var denies = outcomes.Where(static o => o.Decision == Decision.Deny).ToList();
        if (denies.Count > 0)
        {
            return Merge(scope, Decision.Deny, denies, $"{scope} denied by deny-overrides");
        }

        var permits = outcomes.Where(static o => o.Decision == Decision.Permit).ToList();
        if (permits.Count > 0)
        {
            return Merge(scope, Decision.Permit, permits, $"{scope} permitted by deny-overrides");
        }

        var indeterminate = outcomes.Where(static o => o.Decision == Decision.Indeterminate).ToList();
        if (indeterminate.Count > 0)
        {
            return Merge(scope, Decision.Indeterminate, indeterminate, $"{scope} was indeterminate");
        }

        return new NamedDecision(scope, Decision.NotApplicable, [], [], [], $"{scope} had no applicable policies");
    }

    private static NamedDecision CombinePermitOverrides(IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        if (outcomes.Count == 0)
        {
            return new NamedDecision(scope, Decision.NotApplicable, [], [], [], $"{scope} had no applicable policies");
        }

        var permits = outcomes.Where(static o => o.Decision == Decision.Permit).ToList();
        if (permits.Count > 0)
        {
            return Merge(scope, Decision.Permit, permits, $"{scope} permitted by permit-overrides");
        }

        var denies = outcomes.Where(static o => o.Decision == Decision.Deny).ToList();
        if (denies.Count > 0)
        {
            return Merge(scope, Decision.Deny, denies, $"{scope} denied by permit-overrides");
        }

        var indeterminate = outcomes.Where(static o => o.Decision == Decision.Indeterminate).ToList();
        if (indeterminate.Count > 0)
        {
            return Merge(scope, Decision.Indeterminate, indeterminate, $"{scope} was indeterminate");
        }

        return new NamedDecision(scope, Decision.NotApplicable, [], [], [], $"{scope} had no applicable policies");
    }

    private static NamedDecision CombineFirstApplicable(IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        var first = outcomes.FirstOrDefault(static o => o.Decision != Decision.NotApplicable);
        return first is null
            ? new NamedDecision(scope, Decision.NotApplicable, [], [], [], $"{scope} had no applicable policies")
            : first with { Name = scope, Message = $"{scope} resolved by first-applicable" };
    }

    private static NamedDecision CombineOnlyOneApplicable(IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        var applicable = outcomes.Where(static o => o.Decision is Decision.Permit or Decision.Deny).ToList();

        return applicable.Count switch
        {
            0 => new NamedDecision(scope, Decision.NotApplicable, [], [], [], $"{scope} had no applicable policies"),
            1 => applicable[0] with { Name = scope, Message = $"{scope} resolved by only-one-applicable" },
            _ => new NamedDecision(scope, Decision.Indeterminate, [], [], [], $"{scope} matched more than one applicable rule")
        };
    }

    private static NamedDecision CombineDenyUnlessPermit(IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        // Per XACML spec: if any rule evaluates to Permit, the result is Permit.
        // Otherwise the result is always Deny (never NotApplicable or Indeterminate).
        var permits = outcomes.Where(static o => o.Decision == Decision.Permit).ToList();
        if (permits.Count > 0)
        {
            return Merge(scope, Decision.Permit, permits, $"{scope} permitted by deny-unless-permit");
        }

        // Collect obligations/advice from deny outcomes if any exist
        var denies = outcomes.Where(static o => o.Decision == Decision.Deny).ToList();
        if (denies.Count > 0)
        {
            return Merge(scope, Decision.Deny, denies, $"{scope} denied by deny-unless-permit (no permit found)");
        }

        return new NamedDecision(scope, Decision.Deny, [], [], [], $"{scope} denied by deny-unless-permit (default)");
    }

    private static NamedDecision CombinePermitUnlessDeny(IReadOnlyList<NamedDecision> outcomes, string scope)
    {
        // Per XACML spec: if any rule evaluates to Deny, the result is Deny.
        // Otherwise the result is always Permit (never NotApplicable or Indeterminate).
        var denies = outcomes.Where(static o => o.Decision == Decision.Deny).ToList();
        if (denies.Count > 0)
        {
            return Merge(scope, Decision.Deny, denies, $"{scope} denied by permit-unless-deny");
        }

        var permits = outcomes.Where(static o => o.Decision == Decision.Permit).ToList();
        if (permits.Count > 0)
        {
            return Merge(scope, Decision.Permit, permits, $"{scope} permitted by permit-unless-deny (no deny found)");
        }

        return new NamedDecision(scope, Decision.Permit, [], [], [], $"{scope} permitted by permit-unless-deny (default)");
    }

    private static NamedDecision Merge(string scope, Decision decision, IReadOnlyList<NamedDecision> outcomes, string message)
    {
        return new NamedDecision(
            scope,
            decision,
            outcomes.SelectMany(static o => o.AppliedPolicies).Distinct(StringComparer.Ordinal).ToList(),
            outcomes.SelectMany(static o => o.Obligations).ToList(),
            outcomes.SelectMany(static o => o.Advice).ToList(),
            message);
    }

    private static void CollectSubjectAttributes(
        IReadOnlyList<NativeCondition> conditions,
        HashSet<string> attributes)
    {
        foreach (var condition in conditions)
        {
            if (TryGetSubjectAttributeName(condition.Path, out var attributeName))
            {
                attributes.Add(attributeName);
            }

            if (TryGetSubjectAttributeName(condition.EqualsPath, out var equalsAttributeName))
            {
                attributes.Add(equalsAttributeName);
            }
        }
    }

    private static bool TryGetSubjectAttributeName(string? path, out string attributeName)
    {
        attributeName = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "subject", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(parts[1], "id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parts[1], "type", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        attributeName = string.Equals(parts[1], "properties", StringComparison.OrdinalIgnoreCase)
            ? string.Join('.', parts.Skip(2))
            : string.Join('.', parts.Skip(1));

        return !string.IsNullOrWhiteSpace(attributeName);
    }

    private static bool EvaluateCondition(EvaluationRequest request, NativeCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Path))
        {
            return false;
        }

        var found = TryResolvePath(request, condition.Path!, out var actual);
        if (condition.Exists.HasValue)
        {
            return condition.Exists.Value ? found : !found;
        }

        if (!found)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(condition.EqualsPath))
        {
            return TryResolvePath(request, condition.EqualsPath!, out var other)
                && ValuesEqual(actual, other, condition.CaseInsensitive);
        }

        if (condition.EqualsValue is not null)
        {
            return JsonMatches(actual, condition.EqualsValue.Value, condition.CaseInsensitive);
        }

        if (condition.NotEqualsValue is not null)
        {
            return !JsonMatches(actual, condition.NotEqualsValue.Value, condition.CaseInsensitive);
        }

        if (condition.Contains is not null)
        {
            return Contains(actual, condition.Contains.Value, condition.CaseInsensitive);
        }

        if (condition.In is not null && condition.In.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in condition.In.Value.EnumerateArray())
            {
                if (JsonMatches(actual, candidate, condition.CaseInsensitive))
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static bool TryResolvePath(EvaluationRequest request, string path, out object? value)
    {
        value = null;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        object? current = parts[0].ToLowerInvariant() switch
        {
            "subject" => request.Subject,
            "resource" => request.Resource,
            "action" => request.Action,
            "context" => request.Context,
            _ => null
        };

        if (current is null)
        {
            return false;
        }

        for (var i = 1; i < parts.Length; i++)
        {
            if (!TryResolveSegment(current!, parts[i], out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private static bool TryResolveSegment(object current, string segment, out object? next)
    {
        switch (current)
        {
            case SubjectInfo subject when string.Equals(segment, "id", StringComparison.OrdinalIgnoreCase):
                next = subject.Id;
                return true;
            case SubjectInfo subject when string.Equals(segment, "type", StringComparison.OrdinalIgnoreCase):
                next = subject.Type;
                return true;
            case SubjectInfo subject when string.Equals(segment, "properties", StringComparison.OrdinalIgnoreCase):
                next = subject.Properties;
                return true;
            case SubjectInfo subject when subject.Properties.TryGetValue(segment, out var subjectValue):
                next = subjectValue;
                return true;

            case ResourceInfo resource when string.Equals(segment, "id", StringComparison.OrdinalIgnoreCase):
                next = resource.Id;
                return true;
            case ResourceInfo resource when string.Equals(segment, "type", StringComparison.OrdinalIgnoreCase):
                next = resource.Type;
                return true;
            case ResourceInfo resource when string.Equals(segment, "properties", StringComparison.OrdinalIgnoreCase):
                next = resource.Properties;
                return true;
            case ResourceInfo resource when resource.Properties.TryGetValue(segment, out var resourceValue):
                next = resourceValue;
                return true;

            case ActionInfo action when string.Equals(segment, "name", StringComparison.OrdinalIgnoreCase):
                next = action.Name;
                return true;
            case ActionInfo action when string.Equals(segment, "properties", StringComparison.OrdinalIgnoreCase):
                next = action.Properties;
                return true;
            case ActionInfo action when action.Properties.TryGetValue(segment, out var actionValue):
                next = actionValue;
                return true;

            case ContextInfo context when string.Equals(segment, "environment", StringComparison.OrdinalIgnoreCase):
                next = context.Environment;
                return true;
            case ContextInfo context when context.Environment.TryGetValue(segment, out var envValue):
                next = envValue;
                return true;

            case IDictionary<string, object?> dict when dict.TryGetValue(segment, out var dictValue):
                next = dictValue;
                return true;

            default:
                next = null;
                return false;
        }
    }

    private static bool Contains(object? actual, JsonElement expected, bool caseInsensitive)
    {
        if (actual is string actualString && expected.ValueKind == JsonValueKind.String)
        {
            return actualString.Contains(
                expected.GetString() ?? string.Empty,
                caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        if (actual is System.Collections.IEnumerable enumerable && actual is not string)
        {
            foreach (var entry in enumerable)
            {
                if (JsonMatches(entry, expected, caseInsensitive))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool JsonMatches(object? actual, JsonElement expected, bool caseInsensitive)
    {
        return expected.ValueKind switch
        {
            JsonValueKind.String => ValuesEqual(actual, expected.GetString(), caseInsensitive),
            JsonValueKind.Number when expected.TryGetDecimal(out var decimalValue) =>
                decimal.TryParse(Convert.ToString(actual, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var actualDecimal)
                && actualDecimal == decimalValue,
            JsonValueKind.True => ValuesEqual(actual, true, caseInsensitive),
            JsonValueKind.False => ValuesEqual(actual, false, caseInsensitive),
            JsonValueKind.Null => actual is null,
            _ => false
        };
    }

    private static bool ValuesEqual(object? left, object? right, bool caseInsensitive)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is string || right is string)
        {
            return string.Equals(
                Convert.ToString(left, CultureInfo.InvariantCulture),
                Convert.ToString(right, CultureInfo.InvariantCulture),
                caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool == rightBool;
        }

        if (decimal.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var leftDecimal) &&
            decimal.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        return Equals(left, right);
    }

    private static PolicyVersion? SelectVersion(Policy policy, string? requestedVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            return policy.Versions
                .OrderByDescending(static v => v.VersionNumber)
                .FirstOrDefault(v =>
                    string.Equals(v.VersionNumber.ToString(CultureInfo.InvariantCulture), requestedVersion, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.Id.ToString("D"), requestedVersion, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.Id.ToString("N"), requestedVersion, StringComparison.OrdinalIgnoreCase));
        }

        return policy.Versions
            .OrderByDescending(static v => v.VersionNumber)
            .FirstOrDefault(v => v.IsActive || policy.ActiveVersionId == v.Id)
            ?? policy.Versions.OrderByDescending(static v => v.VersionNumber).FirstOrDefault();
    }

    private static bool TryParseDocument(string content, out NativePolicyDocument document, out string? error)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            var root = json.RootElement;
            var combiningAlgorithm = root.TryGetProperty("combiningAlgorithm", out var combiningElement) && combiningElement.ValueKind == JsonValueKind.String
                ? combiningElement.GetString()
                : null;

            var rules = new List<NativeRule>();
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

            document = new NativePolicyDocument(combiningAlgorithm ?? "deny-overrides", rules);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            document = new NativePolicyDocument("deny-overrides", []);
            error = ex.Message;
            return false;
        }
    }

    private static NativeRule ParseRule(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        var effect = element.TryGetProperty("effect", out var effectElement) && effectElement.ValueKind == JsonValueKind.String
            && Enum.TryParse<Decision>(effectElement.GetString(), true, out var parsedEffect)
                ? parsedEffect
                : Decision.Deny;

        var obligations = ParseActionList(element, "obligations", static (itemId, attributes) => new Obligation
        {
            Id = itemId,
            Attributes = attributes
        });

        var advice = ParseActionList(element, "advice", static (itemId, attributes) => new Advice
        {
            Id = itemId,
            Attributes = attributes
        });

        var conditions = new List<NativeCondition>();
        if (element.TryGetProperty("conditions", out var conditionsElement) && conditionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var conditionElement in conditionsElement.EnumerateArray())
            {
                conditions.Add(ParseCondition(conditionElement));
            }
        }

        return new NativeRule(id, effect, conditions, obligations, advice);
    }

    private static NativeCondition ParseCondition(JsonElement element)
    {
        var caseInsensitive = element.TryGetProperty("caseInsensitive", out var ciElement)
            && ciElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && ciElement.GetBoolean();

        return new NativeCondition(
            element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String ? pathElement.GetString() : null,
            element.TryGetProperty("equalsPath", out var equalsPathElement) && equalsPathElement.ValueKind == JsonValueKind.String ? equalsPathElement.GetString() : null,
            element.TryGetProperty("equals", out var equalsElement) ? equalsElement.Clone() : null,
            element.TryGetProperty("notEquals", out var notEqualsElement) ? notEqualsElement.Clone() : null,
            element.TryGetProperty("contains", out var containsElement) ? containsElement.Clone() : null,
            element.TryGetProperty("in", out var inElement) ? inElement.Clone() : null,
            element.TryGetProperty("exists", out var existsElement) && existsElement.ValueKind is JsonValueKind.True or JsonValueKind.False ? existsElement.GetBoolean() : null,
            caseInsensitive);
    }

    private static List<T> ParseActionList<T>(JsonElement element, string propertyName, Func<string, Dictionary<string, object?>, T> factory)
    {
        var result = new List<T>();
        if (!element.TryGetProperty(propertyName, out var actionsElement) || actionsElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in actionsElement.EnumerateArray())
        {
            var itemId = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString() ?? propertyName
                : propertyName;

            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (item.TryGetProperty("attributes", out var attributesElement) && attributesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in attributesElement.EnumerateObject())
                {
                    attributes[property.Name] = ConvertJsonValue(property.Value);
                }
            }

            result.Add(factory(itemId, attributes));
        }

        return result;
    }

    private static object? ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    internal sealed record PolicyEvaluationOutcome(
        Decision Decision,
        List<string> AppliedPolicies,
        List<Obligation> Obligations,
        List<Advice> Advice,
        string? Message,
        List<TraceStep> TraceSteps);

    private sealed record NamedDecision(
        string Name,
        Decision Decision,
        List<string> AppliedPolicies,
        List<Obligation> Obligations,
        List<Advice> Advice,
        string? Message);

    private sealed record NativePolicyDocument(string CombiningAlgorithm, List<NativeRule> Rules);

    private sealed record NativeRule(
        string Id,
        Decision Effect,
        List<NativeCondition> Conditions,
        List<Obligation> Obligations,
        List<Advice> Advice);

    private sealed record NativeCondition(
        string? Path,
        string? EqualsPath,
        JsonElement? EqualsValue,
        JsonElement? NotEqualsValue,
        JsonElement? Contains,
        JsonElement? In,
        bool? Exists,
        bool CaseInsensitive);
}

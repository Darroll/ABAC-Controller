using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Policy;

namespace AbacController.Pdp;

/// <summary>
/// Maps between XACML 3.0 XML format and internal domain models.
/// Supports full round-trip: import (parse) and export (serialize) of
/// PolicySet, Policy, Rule with Target, Condition, Obligation, and Advice.
/// Conforms to the OASIS XACML 3.0 Core specification.
/// </summary>
public static class XacmlXmlMapper
{
    /// <summary>XACML 3.0 policy namespace.</summary>
    private static readonly XNamespace XacmlNs = "urn:oasis:names:tc:xacml:3.0:core:schema:wd-17";

    /// <summary>Standard XACML attribute URN prefixes.</summary>
    private static class XacmlUrns
    {
        public const string SubjectCategory = "urn:oasis:names:tc:xacml:1.0:subject-category:access-subject";
        public const string ResourceCategory = "urn:oasis:names:tc:xacml:3.0:attribute-category:resource";
        public const string ActionCategory = "urn:oasis:names:tc:xacml:3.0:attribute-category:action";
        public const string EnvironmentCategory = "urn:oasis:names:tc:xacml:3.0:attribute-category:environment";

        public const string SubjectIdAttr = "urn:oasis:names:tc:xacml:1.0:subject:subject-id";
        public const string SubjectTypeAttr = "urn:oasis:names:tc:xacml:1.0:subject:subject-type";
        public const string ResourceIdAttr = "urn:oasis:names:tc:xacml:1.0:resource:resource-id";
        public const string ResourceTypeAttr = "urn:oasis:names:tc:xacml:1.0:resource:resource-type";
        public const string ActionIdAttr = "urn:oasis:names:tc:xacml:1.0:action:action-id";

        public const string StringDataType = "http://www.w3.org/2001/XMLSchema#string";
        public const string BooleanDataType = "http://www.w3.org/2001/XMLSchema#boolean";
        public const string IntegerDataType = "http://www.w3.org/2001/XMLSchema#integer";
        public const string DoubleDataType = "http://www.w3.org/2001/XMLSchema#double";

        public const string StringEqual = "urn:oasis:names:tc:xacml:1.0:function:string-equal";
        public const string BooleanEqual = "urn:oasis:names:tc:xacml:1.0:function:boolean-equal";
        public const string IntegerEqual = "urn:oasis:names:tc:xacml:1.0:function:integer-equal";
        public const string StringIsIn = "urn:oasis:names:tc:xacml:1.0:function:string-is-in";
        public const string AnyOf = "urn:oasis:names:tc:xacml:1.0:function:any-of";
    }

    // ─── XACML 3.0 XML Request → EvaluationRequest ───

    /// <summary>
    /// Parse a XACML 3.0 XML request document into an internal <see cref="EvaluationRequest"/>.
    /// </summary>
    /// <param name="xml">Well-formed XACML 3.0 Request XML string.</param>
    /// <returns>The parsed request, or null if the XML is not a valid XACML Request.</returns>
    public static EvaluationRequest? ImportRequest(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            return ImportRequest(doc);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse a XACML 3.0 XML request <see cref="XDocument"/> into an internal <see cref="EvaluationRequest"/>.
    /// </summary>
    public static EvaluationRequest? ImportRequest(XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "Request")
            return null;

        var ns = root.Name.Namespace;
        var subjectProps = new Dictionary<string, object?>(StringComparer.Ordinal);
        var resourceProps = new Dictionary<string, object?>(StringComparer.Ordinal);
        var actionProps = new Dictionary<string, object?>(StringComparer.Ordinal);
        var envProps = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var attributes in root.Elements(ns + "Attributes"))
        {
            var category = attributes.Attribute("Category")?.Value ?? "";
            var targetDict = category switch
            {
                XacmlUrns.SubjectCategory => subjectProps,
                XacmlUrns.ResourceCategory => resourceProps,
                XacmlUrns.ActionCategory => actionProps,
                XacmlUrns.EnvironmentCategory => envProps,
                _ => null
            };

            if (targetDict is null) continue;

            foreach (var attr in attributes.Elements(ns + "Attribute"))
            {
                var attrId = attr.Attribute("AttributeId")?.Value;
                if (string.IsNullOrWhiteSpace(attrId)) continue;

                var valueElement = attr.Element(ns + "AttributeValue");
                if (valueElement is null) continue;

                var dataType = valueElement.Attribute("DataType")?.Value ?? XacmlUrns.StringDataType;
                var value = ParseAttributeValue(valueElement.Value, dataType);
                var shortName = StripXacmlPrefix(attrId);
                targetDict[shortName] = value;
            }
        }

        var subjectId = subjectProps.TryGetValue("subject-id", out var sid) ? sid?.ToString() ?? "" : "";
        var subjectType = subjectProps.TryGetValue("subject-type", out var stype) ? stype?.ToString() ?? "user" : "user";
        var actionName = actionProps.TryGetValue("action-id", out var aid) ? aid?.ToString() ?? "" : "";
        var resourceId = resourceProps.TryGetValue("resource-id", out var rid) ? rid?.ToString() ?? "" : "";
        var resourceType = resourceProps.TryGetValue("resource-type", out var rtype) ? rtype?.ToString() ?? "" : "";

        return new EvaluationRequest
        {
            Subject = new SubjectInfo { Type = subjectType, Id = subjectId, Properties = subjectProps },
            Action = new ActionInfo { Name = actionName, Properties = actionProps },
            Resource = new ResourceInfo { Type = resourceType, Id = resourceId, Properties = resourceProps },
            Context = envProps.Count > 0 ? new ContextInfo { Environment = envProps } : null
        };
    }

    /// <summary>
    /// Export an <see cref="EvaluationRequest"/> to XACML 3.0 XML string.
    /// </summary>
    public static string ExportRequest(EvaluationRequest request)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildRequestElement(request));

        return SerializeDocument(doc);
    }

    // ─── XACML 3.0 XML Response ← EvaluationResult ───

    /// <summary>
    /// Export an <see cref="EvaluationResult"/> to XACML 3.0 XML string.
    /// </summary>
    public static string ExportResponse(EvaluationResult result)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildResponseElement([result]));

        return SerializeDocument(doc);
    }

    /// <summary>
    /// Export a <see cref="BatchEvaluationResult"/> to XACML 3.0 XML string.
    /// </summary>
    public static string ExportBatchResponse(BatchEvaluationResult batchResult)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildResponseElement(batchResult.Evaluations));

        return SerializeDocument(doc);
    }

    /// <summary>
    /// Parse a XACML 3.0 XML response document into a list of <see cref="EvaluationResult"/>.
    /// </summary>
    public static List<EvaluationResult>? ImportResponse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            return ImportResponse(doc);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse a XACML 3.0 XML response <see cref="XDocument"/> into a list of <see cref="EvaluationResult"/>.
    /// </summary>
    public static List<EvaluationResult>? ImportResponse(XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "Response")
            return null;

        var ns = root.Name.Namespace;
        var results = new List<EvaluationResult>();

        foreach (var resultElement in root.Elements(ns + "Result"))
        {
            var decisionStr = resultElement.Element(ns + "Decision")?.Value?.Trim() ?? "Indeterminate";
            var decision = decisionStr switch
            {
                "Permit" => Decision.Permit,
                "Deny" => Decision.Deny,
                "NotApplicable" => Decision.NotApplicable,
                _ => Decision.Indeterminate
            };

            string? statusCode = null;
            string? statusMessage = null;
            var statusElement = resultElement.Element(ns + "Status");
            if (statusElement is not null)
            {
                statusCode = statusElement.Element(ns + "StatusCode")?.Attribute("Value")?.Value;
                statusMessage = statusElement.Element(ns + "StatusMessage")?.Value;
            }

            var obligations = ParseObligationElements(resultElement.Element(ns + "Obligations"), ns);
            var advice = ParseAdviceElements(resultElement.Element(ns + "AssociatedAdvice"), ns);

            results.Add(new EvaluationResult
            {
                DecisionId = Guid.NewGuid().ToString("N"),
                Decision = decision,
                Status = statusCode is not null || statusMessage is not null
                    ? new StatusInfo { Code = statusCode ?? "ok", Message = statusMessage }
                    : null,
                Obligations = obligations,
                Advice = advice
            });
        }

        return results;
    }

    // ─── XACML 3.0 XML PolicySet round-trip ───

    /// <summary>
    /// Export a <see cref="PolicySet"/> domain model to XACML 3.0 XML string.
    /// Serializes the policy set, all policies, and their active versions as rules.
    /// </summary>
    public static string ExportPolicySet(PolicySet policySet)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildPolicySetElement(policySet));

        return SerializeDocument(doc);
    }

    /// <summary>
    /// Import a XACML 3.0 XML PolicySet document into internal <see cref="PolicySet"/> domain model.
    /// </summary>
    /// <param name="xml">XACML 3.0 PolicySet XML string.</param>
    /// <returns>The imported PolicySet, or null if parsing fails.</returns>
    public static PolicySet? ImportPolicySet(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            return ImportPolicySet(doc);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Import a XACML 3.0 XML PolicySet <see cref="XDocument"/> into internal <see cref="PolicySet"/>.
    /// </summary>
    public static PolicySet? ImportPolicySet(XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "PolicySet")
            return null;

        var ns = root.Name.Namespace;
        var policySetId = root.Attribute("PolicySetId")?.Value ?? Guid.NewGuid().ToString("N");
        var combiningAlg = NormalizeCombiningAlgorithm(root.Attribute("PolicyCombiningAlgId")?.Value);

        var description = root.Element(ns + "Description")?.Value;

        var policies = new List<Policy>();
        foreach (var policyElement in root.Elements(ns + "Policy"))
        {
            var policy = ImportPolicyElement(policyElement, ns, policySetId);
            if (policy is not null)
                policies.Add(policy);
        }

        return new PolicySet
        {
            Id = policySetId,
            Name = policySetId,
            Description = description,
            CombiningAlgorithm = combiningAlg,
            Policies = policies
        };
    }

    /// <summary>
    /// Export a single <see cref="Policy"/> to XACML 3.0 XML string.
    /// </summary>
    public static string ExportPolicy(Policy policy, string policySetId)
    {
        var version = policy.Versions
            .OrderByDescending(static v => v.VersionNumber)
            .FirstOrDefault(static v => v.IsActive)
            ?? policy.Versions.OrderByDescending(static v => v.VersionNumber).FirstOrDefault();

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildPolicyElement(policy, version));

        return SerializeDocument(doc);
    }

    // ─── Private helpers: building XML elements ───

    private static XElement BuildRequestElement(EvaluationRequest request)
    {
        var element = new XElement(XacmlNs + "Request",
            new XAttribute("CombinedDecision", "false"),
            new XAttribute("ReturnPolicyIdList", "false"));

        // Subject
        element.Add(BuildAttributesElement(XacmlUrns.SubjectCategory, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [XacmlUrns.SubjectIdAttr] = request.Subject.Id,
            [XacmlUrns.SubjectTypeAttr] = request.Subject.Type
        }.Concat(request.Subject.Properties
            .Where(p => p.Key != "subject-id" && p.Key != "subject-type")
            .Select(p => new KeyValuePair<string, object?>(
                EnsureUrn(p.Key, "urn:oasis:names:tc:xacml:1.0:subject:"), p.Value)))));

        // Resource
        element.Add(BuildAttributesElement(XacmlUrns.ResourceCategory, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [XacmlUrns.ResourceIdAttr] = request.Resource.Id,
            [XacmlUrns.ResourceTypeAttr] = request.Resource.Type
        }.Concat(request.Resource.Properties
            .Where(p => p.Key != "resource-id" && p.Key != "resource-type")
            .Select(p => new KeyValuePair<string, object?>(
                EnsureUrn(p.Key, "urn:oasis:names:tc:xacml:1.0:resource:"), p.Value)))));

        // Action
        element.Add(BuildAttributesElement(XacmlUrns.ActionCategory, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [XacmlUrns.ActionIdAttr] = request.Action.Name
        }.Concat(request.Action.Properties
            .Where(p => p.Key != "action-id")
            .Select(p => new KeyValuePair<string, object?>(
                EnsureUrn(p.Key, "urn:oasis:names:tc:xacml:1.0:action:"), p.Value)))));

        // Environment
        if (request.Context?.Environment is { Count: > 0 })
        {
            element.Add(BuildAttributesElement(XacmlUrns.EnvironmentCategory,
                request.Context.Environment.Select(p => new KeyValuePair<string, object?>(
                    EnsureUrn(p.Key, "urn:oasis:names:tc:xacml:1.0:environment:"), p.Value))));
        }

        return element;
    }

    private static XElement BuildAttributesElement(string category, IEnumerable<KeyValuePair<string, object?>> attributes)
    {
        var element = new XElement(XacmlNs + "Attributes",
            new XAttribute("Category", category));

        foreach (var (attrId, value) in attributes)
        {
            if (value is null) continue;

            var (dataType, strValue) = FormatAttributeValue(value);
            element.Add(new XElement(XacmlNs + "Attribute",
                new XAttribute("AttributeId", attrId),
                new XAttribute("IncludeInResult", "false"),
                new XElement(XacmlNs + "AttributeValue",
                    new XAttribute("DataType", dataType),
                    strValue)));
        }

        return element;
    }

    private static XElement BuildResponseElement(IReadOnlyList<EvaluationResult> results)
    {
        var response = new XElement(XacmlNs + "Response");

        foreach (var result in results)
        {
            var resultElement = new XElement(XacmlNs + "Result",
                new XElement(XacmlNs + "Decision", DecisionToString(result.Decision)));

            // Status
            var statusCode = result.Decision == Decision.Indeterminate
                ? "urn:oasis:names:tc:xacml:1.0:status:processing-error"
                : "urn:oasis:names:tc:xacml:1.0:status:ok";

            resultElement.Add(new XElement(XacmlNs + "Status",
                new XElement(XacmlNs + "StatusCode",
                    new XAttribute("Value", statusCode)),
                result.Status?.Message is not null
                    ? new XElement(XacmlNs + "StatusMessage", result.Status.Message)
                    : null));

            // Obligations
            if (result.Obligations.Count > 0)
            {
                var obligationsElement = new XElement(XacmlNs + "Obligations");
                foreach (var obligation in result.Obligations)
                {
                    var oblElement = new XElement(XacmlNs + "Obligation",
                        new XAttribute("ObligationId", obligation.Id),
                        new XAttribute("FulfillOn", DecisionToString(result.Decision)));

                    foreach (var (key, value) in obligation.Attributes)
                    {
                        if (value is null) continue;
                        var (dataType, strValue) = FormatAttributeValue(value);
                        oblElement.Add(new XElement(XacmlNs + "AttributeAssignment",
                            new XAttribute("AttributeId", key),
                            new XAttribute("DataType", dataType),
                            strValue));
                    }

                    obligationsElement.Add(oblElement);
                }
                resultElement.Add(obligationsElement);
            }

            // Advice
            if (result.Advice.Count > 0)
            {
                var adviceListElement = new XElement(XacmlNs + "AssociatedAdvice");
                foreach (var advice in result.Advice)
                {
                    var adviceElement = new XElement(XacmlNs + "Advice",
                        new XAttribute("AdviceId", advice.Id),
                        new XAttribute("AppliesTo", DecisionToString(result.Decision)));

                    foreach (var (key, value) in advice.Attributes)
                    {
                        if (value is null) continue;
                        var (dataType, strValue) = FormatAttributeValue(value);
                        adviceElement.Add(new XElement(XacmlNs + "AttributeAssignment",
                            new XAttribute("AttributeId", key),
                            new XAttribute("DataType", dataType),
                            strValue));
                    }

                    adviceListElement.Add(adviceElement);
                }
                resultElement.Add(adviceListElement);
            }

            response.Add(resultElement);
        }

        return response;
    }

    private static XElement BuildPolicySetElement(PolicySet policySet)
    {
        var element = new XElement(XacmlNs + "PolicySet",
            new XAttribute("PolicySetId", policySet.Id),
            new XAttribute("PolicyCombiningAlgId", CombiningAlgorithmToUrn(policySet.CombiningAlgorithm)),
            new XAttribute(XNamespace.Xmlns + "xacml", XacmlNs.NamespaceName));

        if (policySet.Description is not null)
            element.Add(new XElement(XacmlNs + "Description", policySet.Description));

        // Target (empty = applies to all)
        element.Add(new XElement(XacmlNs + "Target"));

        foreach (var policy in policySet.Policies)
        {
            var version = policy.Versions
                .OrderByDescending(static v => v.VersionNumber)
                .FirstOrDefault(static v => v.IsActive)
                ?? policy.Versions.OrderByDescending(static v => v.VersionNumber).FirstOrDefault();

            element.Add(BuildPolicyElement(policy, version));
        }

        return element;
    }

    private static XElement BuildPolicyElement(Policy policy, PolicyVersion? version)
    {
        var combiningAlg = "deny-overrides";
        var rules = new List<XElement>();

        if (version?.Content is not null)
        {
            try
            {
                var nativeDoc = System.Text.Json.JsonDocument.Parse(version.Content);
                var root = nativeDoc.RootElement;

                if (root.TryGetProperty("combiningAlgorithm", out var combAlg) &&
                    combAlg.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    combiningAlg = combAlg.GetString() ?? "deny-overrides";
                }

                if (root.TryGetProperty("rules", out var rulesArray) &&
                    rulesArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var ruleJson in rulesArray.EnumerateArray())
                    {
                        rules.Add(BuildRuleFromNativeJson(ruleJson));
                    }
                }
                else
                {
                    rules.Add(BuildRuleFromNativeJson(root));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // If the content is not valid JSON, emit a single deny-all rule
                rules.Add(new XElement(XacmlNs + "Rule",
                    new XAttribute("RuleId", "parse-error"),
                    new XAttribute("Effect", "Deny")));
            }
        }

        var policyElement = new XElement(XacmlNs + "Policy",
            new XAttribute("PolicyId", policy.Id),
            new XAttribute("RuleCombiningAlgId", CombiningAlgorithmToUrn(combiningAlg)),
            new XAttribute("Version", version?.VersionNumber.ToString(CultureInfo.InvariantCulture) ?? "1"));

        policyElement.Add(new XElement(XacmlNs + "Target"));

        foreach (var rule in rules)
            policyElement.Add(rule);

        return policyElement;
    }

    private static XElement BuildRuleFromNativeJson(System.Text.Json.JsonElement ruleJson)
    {
        var ruleId = ruleJson.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String
            ? idEl.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        var effect = ruleJson.TryGetProperty("effect", out var effectEl) && effectEl.ValueKind == System.Text.Json.JsonValueKind.String
            ? effectEl.GetString()?.Trim() ?? "Deny"
            : "Deny";

        // Normalize effect to XACML casing
        effect = effect.ToLowerInvariant() switch
        {
            "permit" => "Permit",
            "deny" => "Deny",
            _ => "Deny"
        };

        var ruleElement = new XElement(XacmlNs + "Rule",
            new XAttribute("RuleId", ruleId),
            new XAttribute("Effect", effect));

        // Conditions → Target + Condition
        if (ruleJson.TryGetProperty("conditions", out var conditionsEl) &&
            conditionsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var targetMatches = new List<XElement>();
            var conditionApplys = new List<XElement>();

            foreach (var cond in conditionsEl.EnumerateArray())
            {
                var path = cond.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? pathEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var (category, attributeId) = ResolvePathToXacmlCategory(path!);

                if (cond.TryGetProperty("equals", out var equalsEl))
                {
                    var (dataType, strValue) = FormatJsonElementValue(equalsEl);
                    var functionId = GetEqualFunctionId(dataType);

                    targetMatches.Add(new XElement(XacmlNs + "Match",
                        new XAttribute("MatchId", functionId),
                        new XElement(XacmlNs + "AttributeValue",
                            new XAttribute("DataType", dataType), strValue),
                        new XElement(XacmlNs + "AttributeDesignator",
                            new XAttribute("Category", category),
                            new XAttribute("AttributeId", attributeId),
                            new XAttribute("DataType", dataType),
                            new XAttribute("MustBePresent", "true"))));
                }
                else if (cond.TryGetProperty("in", out var inEl) && inEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    // Map "in" to an Apply with string-is-in function
                    var apply = new XElement(XacmlNs + "Apply",
                        new XAttribute("FunctionId", XacmlUrns.StringIsIn));

                    apply.Add(new XElement(XacmlNs + "AttributeDesignator",
                        new XAttribute("Category", category),
                        new XAttribute("AttributeId", attributeId),
                        new XAttribute("DataType", XacmlUrns.StringDataType),
                        new XAttribute("MustBePresent", "true")));

                    foreach (var item in inEl.EnumerateArray())
                    {
                        var (_, itemStr) = FormatJsonElementValue(item);
                        apply.Add(new XElement(XacmlNs + "AttributeValue",
                            new XAttribute("DataType", XacmlUrns.StringDataType), itemStr));
                    }

                    conditionApplys.Add(apply);
                }
                else if (cond.TryGetProperty("exists", out var existsEl))
                {
                    // Exists is represented as MustBePresent in AttributeDesignator
                    var mustBePresent = existsEl.ValueKind == System.Text.Json.JsonValueKind.True;
                    targetMatches.Add(new XElement(XacmlNs + "Match",
                        new XAttribute("MatchId", XacmlUrns.StringEqual),
                        new XElement(XacmlNs + "AttributeValue",
                            new XAttribute("DataType", XacmlUrns.StringDataType), "*"),
                        new XElement(XacmlNs + "AttributeDesignator",
                            new XAttribute("Category", category),
                            new XAttribute("AttributeId", attributeId),
                            new XAttribute("DataType", XacmlUrns.StringDataType),
                            new XAttribute("MustBePresent", mustBePresent ? "true" : "false"))));
                }
            }

            if (targetMatches.Count > 0)
            {
                var allOf = new XElement(XacmlNs + "AllOf");
                foreach (var match in targetMatches)
                    allOf.Add(match);

                var anyOf = new XElement(XacmlNs + "AnyOf", allOf);
                ruleElement.Add(new XElement(XacmlNs + "Target", anyOf));
            }

            if (conditionApplys.Count > 0)
            {
                foreach (var apply in conditionApplys)
                {
                    ruleElement.Add(new XElement(XacmlNs + "Condition", apply));
                }
            }
        }

        // Obligations
        if (ruleJson.TryGetProperty("obligations", out var oblsEl) && oblsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var oblJson in oblsEl.EnumerateArray())
            {
                var oblId = oblJson.TryGetProperty("id", out var oblIdEl) && oblIdEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? oblIdEl.GetString() ?? "obligation" : "obligation";
                var oblElement = new XElement(XacmlNs + "ObligationExpression",
                    new XAttribute("ObligationId", oblId),
                    new XAttribute("FulfillOn", effect));

                if (oblJson.TryGetProperty("attributes", out var oblAttrs) && oblAttrs.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in oblAttrs.EnumerateObject())
                    {
                        var (dataType, strValue) = FormatJsonElementValue(prop.Value);
                        oblElement.Add(new XElement(XacmlNs + "AttributeAssignmentExpression",
                            new XAttribute("AttributeId", prop.Name),
                            new XElement(XacmlNs + "AttributeValue",
                                new XAttribute("DataType", dataType), strValue)));
                    }
                }

                ruleElement.Add(oblElement);
            }
        }

        // Advice
        if (ruleJson.TryGetProperty("advice", out var adviceEl) && adviceEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var advJson in adviceEl.EnumerateArray())
            {
                var advId = advJson.TryGetProperty("id", out var advIdEl) && advIdEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? advIdEl.GetString() ?? "advice" : "advice";
                var advElement = new XElement(XacmlNs + "AdviceExpression",
                    new XAttribute("AdviceId", advId),
                    new XAttribute("AppliesTo", effect));

                if (advJson.TryGetProperty("attributes", out var advAttrs) && advAttrs.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in advAttrs.EnumerateObject())
                    {
                        var (dataType, strValue) = FormatJsonElementValue(prop.Value);
                        advElement.Add(new XElement(XacmlNs + "AttributeAssignmentExpression",
                            new XAttribute("AttributeId", prop.Name),
                            new XElement(XacmlNs + "AttributeValue",
                                new XAttribute("DataType", dataType), strValue)));
                    }
                }

                ruleElement.Add(advElement);
            }
        }

        return ruleElement;
    }

    private static Policy? ImportPolicyElement(XElement policyElement, XNamespace ns, string policySetId)
    {
        var policyId = policyElement.Attribute("PolicyId")?.Value ?? Guid.NewGuid().ToString("N");
        var combiningAlg = NormalizeCombiningAlgorithm(policyElement.Attribute("RuleCombiningAlgId")?.Value);
        var versionStr = policyElement.Attribute("Version")?.Value;

        var rules = new List<object>();
        foreach (var ruleElement in policyElement.Elements(ns + "Rule"))
        {
            var nativeRule = ImportRuleElement(ruleElement, ns);
            rules.Add(nativeRule);
        }

        var nativeContent = System.Text.Json.JsonSerializer.Serialize(new
        {
            combiningAlgorithm = combiningAlg,
            rules
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(nativeContent)));

        var versionNumber = 1;
        if (int.TryParse(versionStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVersion))
            versionNumber = parsedVersion;

        var policyVersion = new PolicyVersion
        {
            PolicyId = policyId,
            VersionNumber = versionNumber,
            Content = nativeContent,
            Hash = hash,
            IsActive = true
        };

        return new Policy
        {
            Id = policyId,
            PolicySetId = policySetId,
            Name = policyId,
            Format = "xacml-xml",
            ActiveVersionId = policyVersion.Id,
            Versions = [policyVersion]
        };
    }

    private static object ImportRuleElement(XElement ruleElement, XNamespace ns)
    {
        var ruleId = ruleElement.Attribute("RuleId")?.Value ?? Guid.NewGuid().ToString("N");
        var effect = ruleElement.Attribute("Effect")?.Value?.ToLowerInvariant() ?? "deny";

        var conditions = new List<object>();

        // Parse Target → conditions
        var targetElement = ruleElement.Element(ns + "Target");
        if (targetElement is not null)
        {
            foreach (var anyOf in targetElement.Elements(ns + "AnyOf"))
            {
                foreach (var allOf in anyOf.Elements(ns + "AllOf"))
                {
                    foreach (var match in allOf.Elements(ns + "Match"))
                    {
                        var condition = ImportMatchToCondition(match, ns);
                        if (condition is not null)
                            conditions.Add(condition);
                    }
                }
            }
        }

        // Parse Condition elements
        foreach (var conditionElement in ruleElement.Elements(ns + "Condition"))
        {
            var apply = conditionElement.Element(ns + "Apply");
            if (apply is not null)
            {
                var condition = ImportApplyToCondition(apply, ns);
                if (condition is not null)
                    conditions.Add(condition);
            }
        }

        // Parse obligations
        var obligations = new List<object>();
        foreach (var oblExpr in ruleElement.Elements(ns + "ObligationExpression"))
        {
            obligations.Add(ImportObligationExpression(oblExpr, ns));
        }

        // Parse advice
        var advice = new List<object>();
        foreach (var advExpr in ruleElement.Elements(ns + "AdviceExpression"))
        {
            advice.Add(ImportAdviceExpression(advExpr, ns));
        }

        var rule = new Dictionary<string, object>
        {
            ["id"] = ruleId,
            ["effect"] = effect
        };

        if (conditions.Count > 0)
            rule["conditions"] = conditions;
        if (obligations.Count > 0)
            rule["obligations"] = obligations;
        if (advice.Count > 0)
            rule["advice"] = advice;

        return rule;
    }

    private static object? ImportMatchToCondition(XElement matchElement, XNamespace ns)
    {
        var designator = matchElement.Element(ns + "AttributeDesignator");
        if (designator is null) return null;

        var category = designator.Attribute("Category")?.Value ?? "";
        var attributeId = designator.Attribute("AttributeId")?.Value ?? "";

        var path = XacmlCategoryAndIdToPath(category, attributeId);
        if (string.IsNullOrWhiteSpace(path)) return null;

        var valueElement = matchElement.Element(ns + "AttributeValue");
        if (valueElement is null) return null;

        var dataType = valueElement.Attribute("DataType")?.Value ?? XacmlUrns.StringDataType;
        var value = ParseAttributeValue(valueElement.Value, dataType);

        return new Dictionary<string, object?>
        {
            ["path"] = path,
            ["equals"] = value
        };
    }

    private static object? ImportApplyToCondition(XElement applyElement, XNamespace ns)
    {
        var functionId = applyElement.Attribute("FunctionId")?.Value ?? "";

        // Handle string-is-in → "in" condition
        if (functionId == XacmlUrns.StringIsIn)
        {
            var designator = applyElement.Element(ns + "AttributeDesignator");
            if (designator is null) return null;

            var category = designator.Attribute("Category")?.Value ?? "";
            var attributeId = designator.Attribute("AttributeId")?.Value ?? "";
            var path = XacmlCategoryAndIdToPath(category, attributeId);

            var values = applyElement.Elements(ns + "AttributeValue")
                .Select(v => v.Value)
                .ToList();

            return new Dictionary<string, object?>
            {
                ["path"] = path,
                ["in"] = values
            };
        }

        return null;
    }

    private static object ImportObligationExpression(XElement oblExpr, XNamespace ns)
    {
        var oblId = oblExpr.Attribute("ObligationId")?.Value ?? "obligation";
        var attributes = new Dictionary<string, object?>();

        foreach (var assignment in oblExpr.Elements(ns + "AttributeAssignmentExpression"))
        {
            var attrId = assignment.Attribute("AttributeId")?.Value;
            if (attrId is null) continue;

            var valueEl = assignment.Element(ns + "AttributeValue");
            if (valueEl is null) continue;

            var dataType = valueEl.Attribute("DataType")?.Value ?? XacmlUrns.StringDataType;
            attributes[attrId] = ParseAttributeValue(valueEl.Value, dataType);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = oblId,
            ["attributes"] = attributes
        };
    }

    private static object ImportAdviceExpression(XElement advExpr, XNamespace ns)
    {
        var advId = advExpr.Attribute("AdviceId")?.Value ?? "advice";
        var attributes = new Dictionary<string, object?>();

        foreach (var assignment in advExpr.Elements(ns + "AttributeAssignmentExpression"))
        {
            var attrId = assignment.Attribute("AttributeId")?.Value;
            if (attrId is null) continue;

            var valueEl = assignment.Element(ns + "AttributeValue");
            if (valueEl is null) continue;

            var dataType = valueEl.Attribute("DataType")?.Value ?? XacmlUrns.StringDataType;
            attributes[attrId] = ParseAttributeValue(valueEl.Value, dataType);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = advId,
            ["attributes"] = attributes
        };
    }

    // ─── Utility methods ───

    private static string StripXacmlPrefix(string attributeId)
    {
        ReadOnlySpan<string> prefixes =
        [
            "urn:oasis:names:tc:xacml:1.0:subject:",
            "urn:oasis:names:tc:xacml:3.0:subject:",
            "urn:oasis:names:tc:xacml:1.0:resource:",
            "urn:oasis:names:tc:xacml:3.0:resource:",
            "urn:oasis:names:tc:xacml:1.0:action:",
            "urn:oasis:names:tc:xacml:3.0:action:",
            "urn:oasis:names:tc:xacml:1.0:environment:",
            "urn:oasis:names:tc:xacml:3.0:environment:"
        ];

        foreach (var prefix in prefixes)
        {
            if (attributeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return attributeId[prefix.Length..];
        }

        return attributeId;
    }

    private static string EnsureUrn(string key, string prefix)
    {
        if (key.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            return key;
        return prefix + key;
    }

    private static object? ParseAttributeValue(string textValue, string dataType)
    {
        return dataType switch
        {
            XacmlUrns.BooleanDataType => bool.TryParse(textValue, out var b) ? b : textValue,
            XacmlUrns.IntegerDataType => long.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : textValue,
            XacmlUrns.DoubleDataType => double.TryParse(textValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : textValue,
            _ => textValue
        };
    }

    private static (string DataType, string Value) FormatAttributeValue(object value)
    {
        return value switch
        {
            bool b => (XacmlUrns.BooleanDataType, b.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()),
            int i => (XacmlUrns.IntegerDataType, i.ToString(CultureInfo.InvariantCulture)),
            long l => (XacmlUrns.IntegerDataType, l.ToString(CultureInfo.InvariantCulture)),
            double d => (XacmlUrns.DoubleDataType, d.ToString(CultureInfo.InvariantCulture)),
            float f => (XacmlUrns.DoubleDataType, f.ToString(CultureInfo.InvariantCulture)),
            _ => (XacmlUrns.StringDataType, value.ToString() ?? "")
        };
    }

    private static (string DataType, string Value) FormatJsonElementValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => (XacmlUrns.BooleanDataType, "true"),
            System.Text.Json.JsonValueKind.False => (XacmlUrns.BooleanDataType, "false"),
            System.Text.Json.JsonValueKind.Number when element.TryGetInt64(out var l) =>
                (XacmlUrns.IntegerDataType, l.ToString(CultureInfo.InvariantCulture)),
            System.Text.Json.JsonValueKind.Number =>
                (XacmlUrns.DoubleDataType, element.GetDouble().ToString(CultureInfo.InvariantCulture)),
            _ => (XacmlUrns.StringDataType, element.GetString() ?? element.GetRawText())
        };
    }

    private static string DecisionToString(Decision decision)
    {
        return decision switch
        {
            Decision.Permit => "Permit",
            Decision.Deny => "Deny",
            Decision.NotApplicable => "NotApplicable",
            Decision.Indeterminate => "Indeterminate",
            _ => "Indeterminate"
        };
    }

    private static string CombiningAlgorithmToUrn(string algorithm)
    {
        var normalized = (algorithm ?? "deny-overrides").Trim().ToLowerInvariant();
        return normalized switch
        {
            "deny-overrides" => "urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:deny-overrides",
            "permit-overrides" => "urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:permit-overrides",
            "first-applicable" => "urn:oasis:names:tc:xacml:1.0:rule-combining-algorithm:first-applicable",
            "only-one-applicable" => "urn:oasis:names:tc:xacml:1.0:rule-combining-algorithm:only-one-applicable",
            "deny-unless-permit" => "urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:deny-unless-permit",
            "permit-unless-deny" => "urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:permit-unless-deny",
            _ => "urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:deny-overrides"
        };
    }

    private static string NormalizeCombiningAlgorithm(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
            return "deny-overrides";

        if (urn.Contains("permit-overrides", StringComparison.OrdinalIgnoreCase)) return "permit-overrides";
        if (urn.Contains("first-applicable", StringComparison.OrdinalIgnoreCase)) return "first-applicable";
        if (urn.Contains("only-one-applicable", StringComparison.OrdinalIgnoreCase)) return "only-one-applicable";
        if (urn.Contains("deny-unless-permit", StringComparison.OrdinalIgnoreCase)) return "deny-unless-permit";
        if (urn.Contains("permit-unless-deny", StringComparison.OrdinalIgnoreCase)) return "permit-unless-deny";
        if (urn.Contains("deny-overrides", StringComparison.OrdinalIgnoreCase)) return "deny-overrides";

        return "deny-overrides";
    }

    private static (string Category, string AttributeId) ResolvePathToXacmlCategory(string path)
    {
        var parts = path.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return (XacmlUrns.SubjectCategory, path);

        var segment = parts[0].ToLowerInvariant();
        var remainder = parts.Length > 1 ? parts[1] : parts[0];

        // Map well-known property names
        if (segment == "subject")
        {
            var attrName = MapSubjectProperty(remainder);
            return (XacmlUrns.SubjectCategory, attrName);
        }

        if (segment == "resource")
        {
            var attrName = MapResourceProperty(remainder);
            return (XacmlUrns.ResourceCategory, attrName);
        }

        if (segment == "action")
        {
            var attrName = MapActionProperty(remainder);
            return (XacmlUrns.ActionCategory, attrName);
        }

        if (segment is "context" or "environment")
        {
            var env = parts.Length > 1 ? parts[1] : parts[0];
            if (env.StartsWith("environment.", StringComparison.OrdinalIgnoreCase))
                env = env["environment.".Length..];
            return (XacmlUrns.EnvironmentCategory, EnsureUrn(env, "urn:oasis:names:tc:xacml:1.0:environment:"));
        }

        return (XacmlUrns.SubjectCategory, EnsureUrn(path, "urn:oasis:names:tc:xacml:1.0:subject:"));
    }

    private static string MapSubjectProperty(string property)
    {
        if (property.StartsWith("properties.", StringComparison.OrdinalIgnoreCase))
            property = property["properties.".Length..];

        return property.ToLowerInvariant() switch
        {
            "id" => XacmlUrns.SubjectIdAttr,
            "type" => XacmlUrns.SubjectTypeAttr,
            _ => EnsureUrn(property, "urn:oasis:names:tc:xacml:1.0:subject:")
        };
    }

    private static string MapResourceProperty(string property)
    {
        if (property.StartsWith("properties.", StringComparison.OrdinalIgnoreCase))
            property = property["properties.".Length..];

        return property.ToLowerInvariant() switch
        {
            "id" => XacmlUrns.ResourceIdAttr,
            "type" => XacmlUrns.ResourceTypeAttr,
            _ => EnsureUrn(property, "urn:oasis:names:tc:xacml:1.0:resource:")
        };
    }

    private static string MapActionProperty(string property)
    {
        if (property.StartsWith("properties.", StringComparison.OrdinalIgnoreCase))
            property = property["properties.".Length..];

        return property.ToLowerInvariant() switch
        {
            "name" => XacmlUrns.ActionIdAttr,
            _ => EnsureUrn(property, "urn:oasis:names:tc:xacml:1.0:action:")
        };
    }

    private static string XacmlCategoryAndIdToPath(string category, string attributeId)
    {
        var shortId = StripXacmlPrefix(attributeId);

        return category switch
        {
            XacmlUrns.SubjectCategory => shortId switch
            {
                "subject-id" => "subject.id",
                "subject-type" => "subject.type",
                _ => $"subject.properties.{shortId}"
            },
            XacmlUrns.ResourceCategory => shortId switch
            {
                "resource-id" => "resource.id",
                "resource-type" => "resource.type",
                _ => $"resource.properties.{shortId}"
            },
            XacmlUrns.ActionCategory => shortId switch
            {
                "action-id" => "action.name",
                _ => $"action.properties.{shortId}"
            },
            XacmlUrns.EnvironmentCategory => $"context.environment.{shortId}",
            _ => shortId
        };
    }

    private static string GetEqualFunctionId(string dataType)
    {
        return dataType switch
        {
            XacmlUrns.BooleanDataType => XacmlUrns.BooleanEqual,
            XacmlUrns.IntegerDataType => XacmlUrns.IntegerEqual,
            _ => XacmlUrns.StringEqual
        };
    }

    private static List<Obligation> ParseObligationElements(XElement? obligationsElement, XNamespace ns)
    {
        var results = new List<Obligation>();
        if (obligationsElement is null) return results;

        foreach (var oblElement in obligationsElement.Elements(ns + "Obligation"))
        {
            var id = oblElement.Attribute("ObligationId")?.Value ?? "obligation";
            var attributes = new Dictionary<string, object?>();

            foreach (var assignment in oblElement.Elements(ns + "AttributeAssignment"))
            {
                var attrId = assignment.Attribute("AttributeId")?.Value;
                if (attrId is null) continue;
                var dataType = assignment.Attribute("DataType")?.Value ?? XacmlUrns.StringDataType;
                attributes[attrId] = ParseAttributeValue(assignment.Value, dataType);
            }

            results.Add(new Obligation { Id = id, Attributes = attributes });
        }

        return results;
    }

    private static List<Advice> ParseAdviceElements(XElement? adviceListElement, XNamespace ns)
    {
        var results = new List<Advice>();
        if (adviceListElement is null) return results;

        foreach (var adviceElement in adviceListElement.Elements(ns + "Advice"))
        {
            var id = adviceElement.Attribute("AdviceId")?.Value ?? "advice";
            var attributes = new Dictionary<string, object?>();

            foreach (var assignment in adviceElement.Elements(ns + "AttributeAssignment"))
            {
                var attrId = assignment.Attribute("AttributeId")?.Value;
                if (attrId is null) continue;
                var dataType = assignment.Attribute("DataType")?.Value ?? XacmlUrns.StringDataType;
                attributes[attrId] = ParseAttributeValue(assignment.Value, dataType);
            }

            results.Add(new Advice { Id = id, Attributes = attributes });
        }

        return results;
    }

    private static string SerializeDocument(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false
        }))
        {
            doc.Save(writer);
        }
        return sb.ToString();
    }
}

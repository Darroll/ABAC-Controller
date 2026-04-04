using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;

namespace AbacController.Pdp;

/// <summary>
/// Maps between XACML JSON profile (OASIS TC) format and internal evaluation models.
/// Supports both request/response mapping per the XACML 3.0 JSON Profile specification.
/// </summary>
public static class XacmlJsonMapper
{
    /// <summary>
    /// Map a XACML JSON request to an internal EvaluationRequest.
    /// XACML JSON format:
    /// {
    ///   "Request": {
    ///     "AccessSubject": [{ "Attribute": [{ "AttributeId": "...", "Value": "..." }] }],
    ///     "Resource": [{ "Attribute": [...] }],
    ///     "Action": [{ "Attribute": [...] }],
    ///     "Environment": [{ "Attribute": [...] }]
    ///   }
    /// }
    /// </summary>
    public static EvaluationRequest? MapFromXacmlJson(JsonElement root)
    {
        if (!root.TryGetProperty("Request", out var requestElement))
            return null;

        var subjectProps = ExtractCategory(requestElement, "AccessSubject");
        var resourceProps = ExtractCategory(requestElement, "Resource");
        var actionProps = ExtractCategory(requestElement, "Action");
        var envProps = ExtractCategory(requestElement, "Environment");

        var subjectId = subjectProps.TryGetValue("subject-id", out var sid) ? sid?.ToString() ?? "" : "";
        var subjectType = subjectProps.TryGetValue("subject-type", out var stype) ? stype?.ToString() ?? "user" : "user";
        var actionName = actionProps.TryGetValue("action-id", out var aid) ? aid?.ToString() ?? "" : "";
        var resourceId = resourceProps.TryGetValue("resource-id", out var rid) ? rid?.ToString() ?? "" : "";
        var resourceType = resourceProps.TryGetValue("resource-type", out var rtype) ? rtype?.ToString() ?? "" : "";

        return new EvaluationRequest
        {
            Subject = new SubjectInfo
            {
                Type = subjectType,
                Id = subjectId,
                Properties = subjectProps
            },
            Action = new ActionInfo
            {
                Name = actionName,
                Properties = actionProps
            },
            Resource = new ResourceInfo
            {
                Type = resourceType,
                Id = resourceId,
                Properties = resourceProps
            },
            Context = envProps.Count > 0 ? new ContextInfo { Environment = envProps } : null
        };
    }

    /// <summary>
    /// Map an internal EvaluationResult to XACML JSON response format.
    /// </summary>
    public static object MapToXacmlJsonResponse(EvaluationResult result)
    {
        var response = new Dictionary<string, object>
        {
            ["Response"] = new[]
            {
                BuildResultObject(result)
            }
        };

        return response;
    }

    /// <summary>
    /// Map a batch result to XACML JSON response format.
    /// </summary>
    public static object MapToXacmlJsonBatchResponse(BatchEvaluationResult batchResult)
    {
        var response = new Dictionary<string, object>
        {
            ["Response"] = batchResult.Evaluations.Select(BuildResultObject).ToArray()
        };

        return response;
    }

    private static Dictionary<string, object?> ExtractCategory(JsonElement requestElement, string categoryName)
    {
        var props = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!requestElement.TryGetProperty(categoryName, out var categoryElement))
            return props;

        if (categoryElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in categoryElement.EnumerateArray())
            {
                ExtractAttributes(item, props);
            }
        }
        else if (categoryElement.ValueKind == JsonValueKind.Object)
        {
            ExtractAttributes(categoryElement, props);
        }

        return props;
    }

    private static void ExtractAttributes(JsonElement container, Dictionary<string, object?> props)
    {
        if (!container.TryGetProperty("Attribute", out var attributesElement) ||
            attributesElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var attr in attributesElement.EnumerateArray())
        {
            if (!attr.TryGetProperty("AttributeId", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                continue;

            var attributeId = idElement.GetString()!;
            // Strip standard XACML URN prefixes for convenience
            var shortName = StripXacmlPrefix(attributeId);

            if (attr.TryGetProperty("Value", out var valueElement))
            {
                props[shortName] = ExtractJsonValue(valueElement);
            }
        }
    }

    private static string StripXacmlPrefix(string attributeId)
    {
        var prefixes = new[]
        {
            "urn:oasis:names:tc:xacml:1.0:subject:",
            "urn:oasis:names:tc:xacml:3.0:subject:",
            "urn:oasis:names:tc:xacml:1.0:resource:",
            "urn:oasis:names:tc:xacml:3.0:resource:",
            "urn:oasis:names:tc:xacml:1.0:action:",
            "urn:oasis:names:tc:xacml:3.0:action:",
            "urn:oasis:names:tc:xacml:1.0:environment:",
            "urn:oasis:names:tc:xacml:3.0:environment:"
        };

        foreach (var prefix in prefixes)
        {
            if (attributeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return attributeId[prefix.Length..];
        }

        return attributeId;
    }

    private static object? ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(static e => ExtractJsonValue(e))
                .ToList(),
            _ => element.GetRawText()
        };
    }

    private static object BuildResultObject(EvaluationResult result)
    {
        var resultObj = new Dictionary<string, object>
        {
            ["Decision"] = result.Decision switch
            {
                Decision.Permit => "Permit",
                Decision.Deny => "Deny",
                Decision.NotApplicable => "NotApplicable",
                Decision.Indeterminate => "Indeterminate",
                _ => "Indeterminate"
            }
        };

        if (result.Status is not null)
        {
            resultObj["Status"] = new Dictionary<string, object>
            {
                ["StatusCode"] = new Dictionary<string, string>
                {
                    ["Value"] = result.Decision == Decision.Indeterminate
                        ? "urn:oasis:names:tc:xacml:1.0:status:processing-error"
                        : "urn:oasis:names:tc:xacml:1.0:status:ok"
                },
                ["StatusMessage"] = result.Status.Message ?? ""
            };
        }

        if (result.Obligations.Count > 0)
        {
            resultObj["Obligations"] = result.Obligations.Select(o => new Dictionary<string, object>
            {
                ["Id"] = o.Id,
                ["AttributeAssignment"] = o.Attributes.Select(a => new Dictionary<string, object?>
                {
                    ["AttributeId"] = a.Key,
                    ["Value"] = a.Value
                }).ToArray()
            }).ToArray();
        }

        if (result.Advice.Count > 0)
        {
            resultObj["AssociatedAdvice"] = result.Advice.Select(a => new Dictionary<string, object>
            {
                ["Id"] = a.Id,
                ["AttributeAssignment"] = a.Attributes.Select(attr => new Dictionary<string, object?>
                {
                    ["AttributeId"] = attr.Key,
                    ["Value"] = attr.Value
                }).ToArray()
            }).ToArray();
        }

        return resultObj;
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using AbacController.Api.Observability;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace AbacController.Api.Controllers;

/// <summary>
/// Exposes AuthZEN-compatible REST endpoints for access evaluation, discovery, and related lookup operations.
/// </summary>
[ApiController]
public class AuthZenController : ControllerBase
{
    private readonly IPdpEngine _pdp;
    private readonly ApiMetrics _metrics;
    private readonly AbacDbContext _dbContext;
    private readonly AsyncEvaluationQueue _asyncQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthZenController"/> class.
    /// </summary>
    public AuthZenController(IPdpEngine pdp, ApiMetrics metrics, AbacDbContext dbContext, AsyncEvaluationQueue asyncQueue)
    {
        _pdp = pdp;
        _metrics = metrics;
        _dbContext = dbContext;
        _asyncQueue = asyncQueue;
    }

    /// <summary>
    /// Evaluates a single AuthZEN access request.
    /// </summary>
    [HttpPost("/access/v1/evaluation")]
    [Authorize(Policy = "Evaluate")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> Evaluate(
        [FromBody] AuthZenEvaluationRequest request, CancellationToken ct)
    {
        var internalRequest = MapToInternal(request);
        Response.Headers["X-ABAC-Resource-Key"] = $"{internalRequest.Resource.Type}:{internalRequest.Resource.Id}";
        var result = await _pdp.EvaluateAsync(internalRequest, ct);
        _metrics.RecordEvaluation(result.Decision, result.EvaluationTime);

        var response = new AuthZenEvaluationResponse
        {
            Decision = result.Decision == Decision.Permit,
            Context = new Dictionary<string, object>
            {
                ["id"] = result.DecisionId,
                ["reason_admin"] = result.Decision switch
                {
                    Decision.Permit => $"Permitted by {string.Join(", ", result.AppliedPolicies)}",
                    Decision.Deny => "Denied by policy",
                    Decision.NotApplicable => "No applicable policy (default deny)",
                    Decision.Indeterminate => $"Evaluation error: {result.Status?.Message}",
                    _ => "Unknown"
                }
            }
        };

        Response.Headers["X-ABAC-Decision-Id"] = result.DecisionId;
        Response.Headers["X-ABAC-Evaluation-Time"] = result.EvaluationTime.TotalMilliseconds + "ms";
        Response.Headers["Cache-Control"] = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Evaluates a batch of AuthZEN access requests.
    /// </summary>
    [HttpPost("/access/v1/evaluations")]
    [Authorize(Policy = "Evaluate")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> EvaluateBatch(
        [FromBody] AuthZenBatchRequest request, CancellationToken ct)
    {
        if (request.Evaluations.Count == 0)
            return BadRequest(new { error = "At least one evaluation is required." });

        if (request.Evaluations.Count > 100)
            return BadRequest(new { error = "Batch size exceeds maximum of 100." });

        var results = new List<AuthZenEvaluationResponse>();
        _metrics.RecordBatchEvaluation(request.Evaluations.Count);

        foreach (var eval in request.Evaluations)
        {
            var internalRequest = MapToInternal(eval);
            var result = await _pdp.EvaluateAsync(internalRequest, ct);
            _metrics.RecordEvaluation(result.Decision, result.EvaluationTime);

            results.Add(new AuthZenEvaluationResponse
            {
                Decision = result.Decision == Decision.Permit,
                Context = new Dictionary<string, object>
                {
                    ["id"] = result.DecisionId
                }
            });
        }

        return Ok(new { evaluations = results });
    }

    /// <summary>
    /// Evaluates a request in simulation mode without affecting normal metrics, audit, or cache state.
    /// </summary>
    [HttpPost("/pdp/api/evaluate/simulate")]
    [Authorize(Policy = "EvaluateExplain")]
    public async Task<IActionResult> Simulate(
        [FromBody] AuthZenEvaluationRequest request, CancellationToken ct)
    {
        var internalRequest = MapToInternal(request);
        // Use explain mode to get the trace, but bypass cache so simulation doesn't pollute it
        internalRequest = internalRequest with
        {
            Options = internalRequest.Options with { BypassCache = true }
        };
        var explained = await _pdp.EvaluateExplainAsync(internalRequest, ct);
        // Don't record metrics for simulation

        return Ok(new
        {
            decision = explained.Result.Decision == Decision.Permit,
            simulated = true,
            result = new
            {
                decisionId = explained.Result.DecisionId,
                decision = explained.Result.Decision.ToString(),
                status = explained.Result.Status,
                obligations = explained.Result.Obligations,
                advice = explained.Result.Advice,
                appliedPolicies = explained.Result.AppliedPolicies,
                evaluationTime = explained.Result.EvaluationTime.TotalMilliseconds + "ms",
                attributeProvenance = explained.Result.AttributeProvenance
            },
            trace = new
            {
                policySetId = explained.Trace.PolicySetId,
                policyVersion = explained.Trace.PolicyVersion,
                matchedPolicy = explained.Trace.MatchedPolicy,
                steps = explained.Trace.Steps.Select(s => new
                {
                    ruleId = s.RuleId,
                    effect = s.Effect,
                    result = s.Result,
                    reason = s.Reason
                })
            }
        });
    }

    /// <summary>
    /// Evaluates a request and returns the detailed policy trace used to reach the decision.
    /// </summary>
    [HttpPost("/pdp/api/evaluate/explain")]
    [Authorize(Policy = "EvaluateExplain")]
    public async Task<IActionResult> Explain(
        [FromBody] AuthZenEvaluationRequest request, CancellationToken ct)
    {
        var internalRequest = MapToInternal(request);
        var explained = await _pdp.EvaluateExplainAsync(internalRequest, ct);
        _metrics.RecordEvaluation(explained.Result.Decision, explained.Result.EvaluationTime);

        return Ok(new
        {
            decision = explained.Result.Decision == Decision.Permit,
            context = new Dictionary<string, object>
            {
                ["id"] = explained.Result.DecisionId,
                ["decision"] = explained.Result.Decision.ToString(),
                ["evaluationTime"] = explained.Result.EvaluationTime.TotalMilliseconds + "ms"
            },
            obligations = explained.Result.Obligations,
            advice = explained.Result.Advice,
            appliedPolicies = explained.Result.AppliedPolicies,
            attributeProvenance = explained.Result.AttributeProvenance,
            trace = new
            {
                policySetId = explained.Trace.PolicySetId,
                policyVersion = explained.Trace.PolicyVersion,
                matchedPolicy = explained.Trace.MatchedPolicy,
                steps = explained.Trace.Steps.Select(s => new
                {
                    ruleId = s.RuleId,
                    effect = s.Effect,
                    result = s.Result,
                    reason = s.Reason
                })
            }
        });
    }

    /// <summary>
    /// Queues an evaluation for background processing and delivers the result to the configured callback URL.
    /// </summary>
    [HttpPost("/pdp/api/evaluate/async")]
    [Authorize(Policy = "Evaluate")]
    [EnableRateLimiting("pdp")]
    public IActionResult EvaluateAsync(
        [FromBody] AsyncEvaluationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CallbackUrl))
            return BadRequest(new { error = "callbackUrl is required for async evaluation." });

        if (!Uri.TryCreate(request.CallbackUrl, UriKind.Absolute, out var callbackUri)
            || (callbackUri.Scheme != "https" && callbackUri.Scheme != "http"))
            return BadRequest(new { error = "callbackUrl must be a valid HTTP(S) URL." });

        var evaluationId = Guid.NewGuid().ToString("N");
        var internalRequest = MapToInternal(request);

        _asyncQueue.Enqueue(new AsyncEvaluationJob(
            evaluationId, internalRequest, request.CallbackUrl,
            request.CallbackHeaders ?? new()));

        return Accepted(new
        {
            evaluationId,
            status = "queued",
            callbackUrl = request.CallbackUrl,
            message = "Evaluation has been queued. Result will be delivered via webhook."
        });
    }

    /// <summary>
    /// Returns the current status of a previously queued asynchronous evaluation.
    /// </summary>
    [HttpGet("/pdp/api/evaluate/async/{evaluationId}/status")]
    [Authorize(Policy = "Evaluate")]
    public IActionResult GetAsyncEvaluationStatus(string evaluationId)
    {
        if (_asyncQueue.TryGetResult(evaluationId, out var result))
        {
            return Ok(new
            {
                evaluationId,
                status = "completed",
                result = new AuthZenEvaluationResponse
                {
                    Decision = result!.Decision == Decision.Permit,
                    Context = new Dictionary<string, object>
                    {
                        ["id"] = result.DecisionId,
                        ["evaluationTime"] = result.EvaluationTime.TotalMilliseconds + "ms"
                    }
                }
            });
        }

        if (_asyncQueue.IsQueued(evaluationId))
        {
            return Ok(new { evaluationId, status = "processing" });
        }

        return NotFound(new { evaluationId, status = "not_found" });
    }

    /// <summary>
    /// Returns the AuthZEN discovery document advertised by this server.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/.well-known/authzen-configuration")]
    public IActionResult GetConfiguration()
    {
        return Ok(new
        {
            issuer = $"{Request.Scheme}://{Request.Host}",
            evaluation_endpoint = "/access/v1/evaluation",
            evaluations_endpoint = "/access/v1/evaluations",
            evaluation_explain_endpoint = "/pdp/api/evaluate/explain",
            evaluation_simulate_endpoint = "/pdp/api/evaluate/simulate",
            evaluation_async_endpoint = "/pdp/api/evaluate/async",
            subjects_endpoint = "/access/v1/subjects",
            resources_endpoint = "/access/v1/resources",
            actions_endpoint = "/access/v1/actions",
            authentication_methods = new[] { "bearer" },
            api_version = "1.0"
        });
    }

    /// <summary>
    /// Evaluates a single request expressed using the XACML JSON profile.
    /// </summary>
    [HttpPost("/access/v1/xacml-json")]
    [Authorize(Policy = "Evaluate")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> EvaluateXacmlJson(
        [FromBody] JsonElement xacmlRequest, CancellationToken ct)
    {
        var internalRequest = AbacController.Pdp.XacmlJsonMapper.MapFromXacmlJson(xacmlRequest);
        if (internalRequest is null)
            return BadRequest(new { error = "Invalid XACML JSON request. Expected { \"Request\": { ... } }" });

        var result = await _pdp.EvaluateAsync(internalRequest, ct);
        _metrics.RecordEvaluation(result.Decision, result.EvaluationTime);

        return Ok(AbacController.Pdp.XacmlJsonMapper.MapToXacmlJsonResponse(result));
    }

    /// <summary>
    /// Searches subject identifiers and types observed in audit history.
    /// </summary>
    [HttpPost("/access/v1/subjects")]
    [Authorize(Policy = "Evaluate")]
    public async Task<IActionResult> SearchSubjects([FromBody] AuthZenSearchRequest? request, CancellationToken ct)
    {
        var q = request?.Query;
        var query = _dbContext.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(e => (e.SubjectId ?? string.Empty).Contains(q) || (e.SubjectType ?? string.Empty).Contains(q));
        }

        var items = await query
            .Where(e => e.SubjectId != null)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new { id = e.SubjectId!, type = e.SubjectType ?? "subject" })
            .Distinct()
            .Take(100)
            .ToListAsync(ct);

        return Ok(new { subjects = items });
    }

    /// <summary>
    /// Searches resource identifiers and types observed in audit history.
    /// </summary>
    [HttpPost("/access/v1/resources")]
    [Authorize(Policy = "Evaluate")]
    public async Task<IActionResult> SearchResources([FromBody] AuthZenSearchRequest? request, CancellationToken ct)
    {
        var q = request?.Query;
        var query = _dbContext.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(e => (e.ResourceId ?? string.Empty).Contains(q) || (e.ResourceType ?? string.Empty).Contains(q));
        }

        var items = await query
            .Where(e => e.ResourceId != null)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new { id = e.ResourceId!, type = e.ResourceType ?? "resource" })
            .Distinct()
            .Take(100)
            .ToListAsync(ct);

        return Ok(new { resources = items });
    }

    /// <summary>
    /// Searches action names observed in audit history.
    /// </summary>
    [HttpPost("/access/v1/actions")]
    [Authorize(Policy = "Evaluate")]
    public async Task<IActionResult> SearchActions([FromBody] AuthZenSearchRequest? request, CancellationToken ct)
    {
        var q = request?.Query;
        var query = _dbContext.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(e => (e.ActionName ?? string.Empty).Contains(q));
        }

        var items = await query
            .Where(e => e.ActionName != null)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => e.ActionName!)
            .Distinct()
            .Take(100)
            .ToListAsync(ct);

        return Ok(new { actions = items.Select(name => new { name }) });
    }

    /// <summary>Search query for AuthZEN entity endpoints.</summary>
    /// <param name="Query">Optional text filter.</param>
    /// <param name="Limit">Maximum results to return.</param>
    public sealed record AuthZenSearchRequest(string? Query = null, int? Limit = null);

    /// <summary>Maps an AuthZEN evaluation request to the internal domain model.</summary>
    private static EvaluationRequest MapToInternal(AuthZenEvaluationRequest request)
    {
        var subjectProperties = request.Subject?.Properties is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(request.Subject.Properties, StringComparer.Ordinal);

        var resourceProperties = request.Resource?.Properties is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(request.Resource.Properties, StringComparer.Ordinal);

        if (TryParseSecurityClearance(subjectProperties, out var securityClearance))
        {
            subjectProperties["securityClearance"] = securityClearance;
        }

        if (TryParseSecurityLabel(resourceProperties, out var securityLabel))
        {
            resourceProperties["securityLabel"] = securityLabel;
            if (!string.IsNullOrWhiteSpace(securityLabel.PolicyOid))
            {
                resourceProperties["securityLabel.policyOid"] = securityLabel.PolicyOid;
            }
        }

        return new EvaluationRequest
        {
            RequestId = request.RequestId,
            Subject = new SubjectInfo
            {
                Type = request.Subject?.Type ?? "user",
                Id = request.Subject?.Id ?? "",
                Properties = subjectProperties
            },
            Action = new ActionInfo
            {
                Name = request.Action?.Name ?? "",
                Properties = request.Action?.Properties ?? new()
            },
            Resource = new ResourceInfo
            {
                Type = request.Resource?.Type ?? "",
                Id = request.Resource?.Id ?? "",
                Properties = resourceProperties
            }
        };
    }

    /// <summary>Attempts to parse a <see cref="SecurityClearance"/> from subject properties.</summary>
    private static bool TryParseSecurityClearance(
        IReadOnlyDictionary<string, object?> properties,
        out SecurityClearance clearance)
    {
        clearance = null!;
        if (!properties.TryGetValue("securityClearance", out var raw) || raw is null)
        {
            return false;
        }

        var element = ToJsonElement(raw);
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("policyOid", out var policyOidElement) ||
            string.IsNullOrWhiteSpace(policyOidElement.GetString()))
        {
            return false;
        }

        var classificationLacvs = element.TryGetProperty("classificationLacvs", out var classificationElement) && classificationElement.ValueKind == JsonValueKind.Array
            ? classificationElement.EnumerateArray()
                .Where(static entry => entry.TryGetInt32(out _))
                .Select(static entry => (LacvValue)entry.GetInt32())
                .ToImmutableHashSet()
            : ImmutableHashSet<LacvValue>.Empty;

        var categoryTagSets = new List<ClearanceCategoryTagSet>();
        if (element.TryGetProperty("categoryTagSets", out var categoryTagSetsElement) && categoryTagSetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagSetElement in categoryTagSetsElement.EnumerateArray())
            {
                if (!tagSetElement.TryGetProperty("tagSetOid", out var tagSetOidElement) || string.IsNullOrWhiteSpace(tagSetOidElement.GetString()))
                {
                    continue;
                }

                var tags = new List<ClearanceCategoryTag>();
                if (tagSetElement.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tagElement in tagsElement.EnumerateArray())
                    {
                        tags.Add(new ClearanceCategoryTag
                        {
                            TagOid = tagElement.TryGetProperty("tagOid", out var tagOidElement) ? tagOidElement.GetString() : null,
                            TagType = ParseTagType(tagElement.TryGetProperty("tagType", out var tagTypeElement) ? tagTypeElement.GetString() : null),
                            Bits = ParseLacvSet(tagElement, "bits"),
                            EnumeratedValues = ParseLacvSet(tagElement, "enumeratedValues")
                        });
                    }
                }

                categoryTagSets.Add(new ClearanceCategoryTagSet
                {
                    TagSetOid = tagSetOidElement.GetString()!,
                    Tags = tags.ToImmutableList()
                });
            }
        }

        clearance = new SecurityClearance
        {
            PolicyOid = policyOidElement.GetString()!,
            ClassificationLacvs = classificationLacvs,
            CategoryTagSets = categoryTagSets.ToImmutableList()
        };

        return true;
    }

    /// <summary>Attempts to parse a <see cref="SecurityLabel"/> from resource properties.</summary>
    private static bool TryParseSecurityLabel(
        IReadOnlyDictionary<string, object?> properties,
        out SecurityLabel label)
    {
        label = null!;
        if (!properties.TryGetValue("securityLabel", out var raw) || raw is null)
        {
            return false;
        }

        var element = ToJsonElement(raw);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var categoryTagSets = new List<LabelCategoryTagSet>();
        if (element.TryGetProperty("categoryTagSets", out var categoryTagSetsElement) && categoryTagSetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagSetElement in categoryTagSetsElement.EnumerateArray())
            {
                if (!tagSetElement.TryGetProperty("tagSetOid", out var tagSetOidElement) || string.IsNullOrWhiteSpace(tagSetOidElement.GetString()))
                {
                    continue;
                }

                var tags = new List<LabelCategoryTag>();
                if (tagSetElement.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tagElement in tagsElement.EnumerateArray())
                    {
                        var categories = new List<LabelCategory>();
                        if (tagElement.TryGetProperty("categories", out var categoriesElement) && categoriesElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var categoryElement in categoriesElement.EnumerateArray())
                            {
                                if (!categoryElement.TryGetProperty("name", out var categoryNameElement) || !categoryElement.TryGetProperty("lacv", out var lacvElement) || !lacvElement.TryGetInt32(out var lacvValue))
                                {
                                    continue;
                                }

                                categories.Add(new LabelCategory
                                {
                                    Name = categoryNameElement.GetString() ?? string.Empty,
                                    Lacv = lacvValue,
                                    NotBefore = ParseOptionalDateTimeOffset(categoryElement, "notBefore"),
                                    NotAfter = ParseOptionalDateTimeOffset(categoryElement, "notAfter")
                                });
                            }
                        }

                        tags.Add(new LabelCategoryTag
                        {
                            Name = tagElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null,
                            TagOid = tagElement.TryGetProperty("tagOid", out var tagOidElement) ? tagOidElement.GetString() : null,
                            TagType = ParseTagType(tagElement.TryGetProperty("tagType", out var tagTypeElement) ? tagTypeElement.GetString() : null),
                            EnumType = ParseOptionalEnumType(tagElement.TryGetProperty("enumType", out var enumTypeElement) ? enumTypeElement.GetString() : null),
                            Bits = ParseLacvSet(tagElement, "bits"),
                            EnumeratedValues = ParseLacvSet(tagElement, "enumeratedValues"),
                            Categories = categories.ToImmutableList()
                        });
                    }
                }

                categoryTagSets.Add(new LabelCategoryTagSet
                {
                    TagSetOid = tagSetOidElement.GetString()!,
                    Tags = tags.ToImmutableList()
                });
            }
        }

        var privacyMarks = element.TryGetProperty("privacyMarks", out var privacyMarksElement) && privacyMarksElement.ValueKind == JsonValueKind.Array
            ? privacyMarksElement.EnumerateArray().Where(static entry => entry.ValueKind == JsonValueKind.String).Select(static entry => entry.GetString()!).ToImmutableList()
            : ImmutableList<string>.Empty;

        label = new SecurityLabel
        {
            PolicyOid = element.TryGetProperty("policyOid", out var policyOidElement) ? policyOidElement.GetString() : null,
            PolicyName = element.TryGetProperty("policyName", out var policyNameElement) ? policyNameElement.GetString() : null,
            ClassificationLacv = element.TryGetProperty("classificationLacv", out var classificationLacvElement) && classificationLacvElement.TryGetInt32(out var classificationLacv)
                ? classificationLacv
                : 0,
            ClassificationName = element.TryGetProperty("classificationName", out var classificationNameElement) ? classificationNameElement.GetString() : null,
            CategoryTagSets = categoryTagSets.ToImmutableList(),
            PrivacyMarks = privacyMarks,
            CreatedAt = ParseOptionalDateTimeOffset(element, "createdAt")
        };

        return true;
    }

    /// <summary>Parses an array of LACV integers from a JSON property.</summary>
    private static ImmutableHashSet<LacvValue> ParseLacvSet(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valuesElement) || valuesElement.ValueKind != JsonValueKind.Array)
        {
            return ImmutableHashSet<LacvValue>.Empty;
        }

        return valuesElement.EnumerateArray()
            .Where(static value => value.TryGetInt32(out _))
            .Select(static value => (LacvValue)value.GetInt32())
            .ToImmutableHashSet();
    }

    /// <summary>Parses an optional ISO 8601 date-time from a JSON property.</summary>
    private static DateTimeOffset? ParseOptionalDateTimeOffset(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement)
            && propertyElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(propertyElement.GetString(), out var parsed)
                ? parsed
                : null;

    /// <summary>Parses a tag type string, defaulting to <see cref="TagType.Restrictive"/>.</summary>
    private static TagType ParseTagType(string? value)
        => Enum.TryParse<TagType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : TagType.Restrictive;

    /// <summary>Parses an optional enum type string.</summary>
    private static EnumType? ParseOptionalEnumType(string? value)
        => Enum.TryParse<EnumType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : null;

    /// <summary>Converts an arbitrary object to a <see cref="JsonElement"/>.</summary>
    private static JsonElement ToJsonElement(object raw)
    {
        if (raw is JsonElement element)
        {
            return element;
        }

        var json = JsonSerializer.Serialize(raw);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

// ── AuthZEN DTOs ──

/// <summary>AuthZEN 1.0 evaluation request.</summary>
public class AuthZenEvaluationRequest
{
    /// <summary>Caller-provided request correlation ID.</summary>
    public string? RequestId { get; set; }

    /// <summary>Subject of the access request.</summary>
    public AuthZenSubject? Subject { get; set; }

    /// <summary>Action being requested.</summary>
    public AuthZenAction? Action { get; set; }

    /// <summary>Resource being accessed.</summary>
    public AuthZenResource? Resource { get; set; }
}

/// <summary>Subject entity in an AuthZEN request.</summary>
public class AuthZenSubject
{
    /// <summary>Subject type (e.g. "user", "service").</summary>
    public string Type { get; set; } = "user";

    /// <summary>Subject identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Additional subject attributes.</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>Action entity in an AuthZEN request.</summary>
public class AuthZenAction
{
    /// <summary>Action name (e.g. "read", "write", "delete").</summary>
    public string Name { get; set; } = "";

    /// <summary>Additional action attributes.</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>Resource entity in an AuthZEN request.</summary>
public class AuthZenResource
{
    /// <summary>Resource type.</summary>
    public string Type { get; set; } = "";

    /// <summary>Resource identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Additional resource attributes (may include securityLabel).</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>AuthZEN 1.0 evaluation response.</summary>
public class AuthZenEvaluationResponse
{
    /// <summary>Whether access is permitted.</summary>
    public bool Decision { get; set; }

    /// <summary>Additional context (decision ID, reason, etc.).</summary>
    public Dictionary<string, object> Context { get; set; } = new();
}

/// <summary>AuthZEN 1.0 batch evaluation request.</summary>
public class AuthZenBatchRequest
{
    /// <summary>Individual evaluation requests in this batch.</summary>
    public List<AuthZenEvaluationRequest> Evaluations { get; set; } = [];
}

/// <summary>
/// Request for async evaluation with webhook callback.
/// </summary>
public class AsyncEvaluationRequest : AuthZenEvaluationRequest
{
    /// <summary>URL to receive the evaluation result via HTTP POST.</summary>
    public string CallbackUrl { get; set; } = "";

    /// <summary>Optional headers to include in the callback request.</summary>
    public Dictionary<string, string>? CallbackHeaders { get; set; }
}

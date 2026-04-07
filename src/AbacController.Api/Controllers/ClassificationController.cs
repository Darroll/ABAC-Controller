using AbacController.Api.Observability;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AbacController.Api.Controllers;

/// <summary>
/// Exposes classification query endpoints for determining which classifications
/// a subject is permitted to assign in a given application and context.
/// </summary>
[ApiController]
public sealed class ClassificationController : ControllerBase
{
    private readonly IClassificationQueryEngine _engine;
    private readonly ApiMetrics _metrics;

    public ClassificationController(IClassificationQueryEngine engine, ApiMetrics metrics)
    {
        _engine = engine;
        _metrics = metrics;
    }

    /// <summary>
    /// Returns the set of classifications a subject is permitted to assign
    /// in the specified application and context.
    /// </summary>
    [HttpPost("/pdp/api/classifications/allowed")]
    [Authorize(Policy = "ClassificationQuery")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> GetAllowedClassifications(
        [FromBody] AllowedClassificationsRequest request, CancellationToken ct)
    {
        if (request.Subject is null)
            return BadRequest(new { error = "Subject is required." });

        var query = MapToQuery(request);
        var result = await _engine.EvaluateAsync(query, ct);

        Response.Headers["X-ABAC-Result-Id"] = result.ResultId;
        Response.Headers["X-ABAC-Evaluation-Time"] = result.EvaluationTime.TotalMilliseconds + "ms";
        Response.Headers["Cache-Control"] = "no-store";

        return Ok(result);
    }

    /// <summary>
    /// Batch query for allowed classifications across multiple subjects/contexts.
    /// </summary>
    [HttpPost("/pdp/api/classifications/allowed/batch")]
    [Authorize(Policy = "ClassificationQuery")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> GetAllowedClassificationsBatch(
        [FromBody] AllowedClassificationsBatchRequest request, CancellationToken ct)
    {
        if (request.Queries is null || request.Queries.Count == 0)
            return BadRequest(new { error = "At least one query is required." });

        if (request.Queries.Count > 50)
            return BadRequest(new { error = "Batch size exceeds maximum of 50." });

        var queries = request.Queries.Select(MapToQuery).ToList();
        var results = await _engine.EvaluateBatchAsync(queries, ct);

        return Ok(new { results });
    }

    private static ClassificationAssignmentQuery MapToQuery(AllowedClassificationsRequest request)
    {
        var subjectProperties = request.Subject?.Properties is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(request.Subject.Properties, StringComparer.Ordinal);

        // Parse security clearance from subject properties if present
        if (SecurityModelParser.TryParseSecurityClearance(subjectProperties, out var clearance))
        {
            subjectProperties["securityClearance"] = clearance;
        }

        return new ClassificationAssignmentQuery
        {
            RequestId = request.RequestId,
            Subject = new SubjectInfo
            {
                Type = request.Subject?.Type ?? "user",
                Id = request.Subject?.Id ?? "",
                Properties = subjectProperties
            },
            ApplicationId = request.ApplicationId,
            PolicyOidOverride = request.PolicyOidOverride,
            ActionName = request.ActionName ?? "classify",
            Resource = request.Resource is not null
                ? new ResourceContext
                {
                    Type = request.Resource.Type,
                    Id = request.Resource.Id,
                    Properties = request.Resource.Properties ?? new()
                }
                : null,
            Environment = request.Environment ?? new(),
            IncludeMarkingData = request.IncludeMarkingData,
            IncludeCategories = request.IncludeCategories ?? true,
            IncludeTrace = request.IncludeTrace,
            PolicySetId = request.PolicySetId
        };
    }

    // ── Request DTOs ──

    /// <summary>Request to query allowed classifications for a subject.</summary>
    public sealed class AllowedClassificationsRequest
    {
        /// <summary>Correlation ID.</summary>
        public string? RequestId { get; set; }

        /// <summary>Subject being evaluated.</summary>
        public ClassificationSubject? Subject { get; set; }

        /// <summary>Application scope filter.</summary>
        public string? ApplicationId { get; set; }

        /// <summary>Explicit SPIF policy OID override.</summary>
        public string? PolicyOidOverride { get; set; }

        /// <summary>Action name for policy context (default: "classify").</summary>
        public string? ActionName { get; set; }

        /// <summary>Optional resource context.</summary>
        public ClassificationResource? Resource { get; set; }

        /// <summary>Environment attributes.</summary>
        public Dictionary<string, object?>? Environment { get; set; }

        /// <summary>Include SPIF marking data per classification.</summary>
        public bool IncludeMarkingData { get; set; }

        /// <summary>Include allowed categories per classification (default: true).</summary>
        public bool? IncludeCategories { get; set; }

        /// <summary>Include diagnostic filter trace.</summary>
        public bool IncludeTrace { get; set; }

        /// <summary>Target specific policy set.</summary>
        public string? PolicySetId { get; set; }
    }

    /// <summary>Subject in a classification query.</summary>
    public sealed class ClassificationSubject
    {
        /// <summary>Subject type.</summary>
        public string Type { get; set; } = "user";

        /// <summary>Subject identifier.</summary>
        public string Id { get; set; } = "";

        /// <summary>Subject properties (may include securityClearance).</summary>
        public Dictionary<string, object?> Properties { get; set; } = new();
    }

    /// <summary>Resource context in a classification query.</summary>
    public sealed class ClassificationResource
    {
        /// <summary>Resource type (e.g., "email").</summary>
        public string? Type { get; set; }

        /// <summary>Resource identifier.</summary>
        public string? Id { get; set; }

        /// <summary>Additional resource properties.</summary>
        public Dictionary<string, object?>? Properties { get; set; }
    }

    /// <summary>Batch request for allowed classifications.</summary>
    public sealed class AllowedClassificationsBatchRequest
    {
        /// <summary>Individual queries.</summary>
        public List<AllowedClassificationsRequest> Queries { get; set; } = [];
    }
}

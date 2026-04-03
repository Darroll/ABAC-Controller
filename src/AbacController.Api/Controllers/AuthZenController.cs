using System.Text.Json;
using AbacController.Api.Observability;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// AuthZEN 1.0 REST endpoints for access evaluation.
/// </summary>
[ApiController]
public class AuthZenController : ControllerBase
{
    private readonly IPdpEngine _pdp;
    private readonly ApiMetrics _metrics;
    private readonly AbacDbContext _dbContext;

    public AuthZenController(IPdpEngine pdp, ApiMetrics metrics, AbacDbContext dbContext)
    {
        _pdp = pdp;
        _metrics = metrics;
        _dbContext = dbContext;
    }

    /// <summary>
    /// AuthZEN 1.0 single evaluation.
    /// POST /access/v1/evaluation
    /// </summary>
    [HttpPost("/access/v1/evaluation")]
    [Authorize(Policy = "Evaluate")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> Evaluate(
        [FromBody] AuthZenEvaluationRequest request, CancellationToken ct)
    {
        var internalRequest = MapToInternal(request);
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
    /// AuthZEN 1.0 batch evaluation.
    /// POST /access/v1/evaluations
    /// </summary>
    [HttpPost("/access/v1/evaluations")]
    [Authorize(Policy = "Evaluate")]
    [EnableRateLimiting("pdp")]
    public async Task<IActionResult> EvaluateBatch(
        [FromBody] AuthZenBatchRequest request, CancellationToken ct)
    {
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
    /// AuthZEN 1.0 discovery endpoint.
    /// GET /.well-known/authzen-configuration
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
            subjects_endpoint = "/access/v1/subjects",
            resources_endpoint = "/access/v1/resources",
            actions_endpoint = "/access/v1/actions",
            authentication_methods = new[] { "bearer" },
            api_version = "1.0"
        });
    }

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

    public sealed record AuthZenSearchRequest(string? Query = null, int? Limit = null);

    private static EvaluationRequest MapToInternal(AuthZenEvaluationRequest request) => new()
    {
        RequestId = request.RequestId,
        Subject = new SubjectInfo
        {
            Type = request.Subject?.Type ?? "user",
            Id = request.Subject?.Id ?? "",
            Properties = request.Subject?.Properties ?? new()
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
            Properties = request.Resource?.Properties ?? new()
        }
    };
}

// ── AuthZEN DTOs ──

public class AuthZenEvaluationRequest
{
    public string? RequestId { get; set; }
    public AuthZenSubject? Subject { get; set; }
    public AuthZenAction? Action { get; set; }
    public AuthZenResource? Resource { get; set; }
}

public class AuthZenSubject
{
    public string Type { get; set; } = "user";
    public string Id { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public class AuthZenAction
{
    public string Name { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public class AuthZenResource
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public class AuthZenEvaluationResponse
{
    public bool Decision { get; set; }
    public Dictionary<string, object> Context { get; set; } = new();
}

public class AuthZenBatchRequest
{
    public List<AuthZenEvaluationRequest> Evaluations { get; set; } = [];
}

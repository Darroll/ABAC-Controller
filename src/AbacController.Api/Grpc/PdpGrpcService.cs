using AbacController.Api.Observability;
using AbacController.Core.Interfaces;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AbacController.Api.Grpc;

[Authorize(Policy = "Evaluate")]
/// <summary>
/// Implements the PDP gRPC surface for single, batch, and explained evaluations.
/// </summary>
[EnableRateLimiting("pdp")]
public sealed class PdpGrpcService : PdpApi.PdpApiBase
{
    private readonly IPdpEngine _pdpEngine;
    private readonly ApiMetrics _metrics;

    /// <summary>Initializes a new instance of the <see cref="PdpGrpcService"/> class.</summary>
    public PdpGrpcService(IPdpEngine pdpEngine, ApiMetrics metrics)
    {
        _pdpEngine = pdpEngine;
        _metrics = metrics;
    }

    /// <summary>Evaluates a single authorization request.</summary>
    public override async Task<EvaluateResponseMessage> Evaluate(EvaluateRequestMessage request, ServerCallContext context)
    {
        var result = await _pdpEngine.EvaluateAsync(ProtoMapper.ToDomain(request), context.CancellationToken);
        _metrics.RecordEvaluation(result.Decision, result.EvaluationTime);
        return ProtoMapper.ToProto(result);
    }

    /// <summary>Evaluates a batch of authorization requests.</summary>
    public override async Task<EvaluateBatchResponseMessage> EvaluateBatch(EvaluateBatchRequestMessage request, ServerCallContext context)
    {
        var result = await _pdpEngine.EvaluateBatchAsync(ProtoMapper.ToDomain(request), context.CancellationToken);
        _metrics.RecordBatchEvaluation(result.Evaluations.Count);
        foreach (var evaluation in result.Evaluations)
        {
            _metrics.RecordEvaluation(evaluation.Decision, evaluation.EvaluationTime);
        }

        return ProtoMapper.ToProto(result);
    }

    [Authorize(Policy = "EvaluateExplain")]
    /// <summary>Evaluates a request and returns the corresponding decision trace.</summary>
    public override async Task<ExplainedEvaluateResponseMessage> EvaluateExplain(EvaluateRequestMessage request, ServerCallContext context)
    {
        var result = await _pdpEngine.EvaluateExplainAsync(ProtoMapper.ToDomain(request), context.CancellationToken);
        _metrics.RecordEvaluation(result.Result.Decision, result.Result.EvaluationTime);
        return ProtoMapper.ToProto(result);
    }
}

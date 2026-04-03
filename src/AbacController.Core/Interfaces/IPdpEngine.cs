using AbacController.Core.Domain.Decisions;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Core PDP evaluation interface. Orchestrates policy lookup, attribute resolution,
/// ACDF evaluation, and decision assembly.
/// </summary>
public interface IPdpEngine
{
    /// <summary>Evaluate a single authorization request.</summary>
    Task<EvaluationResult> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken ct = default);

    /// <summary>Evaluate a batch of requests sharing a common subject.</summary>
    Task<BatchEvaluationResult> EvaluateBatchAsync(
        BatchEvaluationRequest request,
        CancellationToken ct = default);

    /// <summary>Evaluate with full explanation trace.</summary>
    Task<ExplainedEvaluationResult> EvaluateExplainAsync(
        EvaluationRequest request,
        CancellationToken ct = default);
}

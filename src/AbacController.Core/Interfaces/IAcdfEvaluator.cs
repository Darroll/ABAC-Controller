using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;

namespace AbacController.Core.Interfaces;

/// <summary>
/// The ACDF evaluator. Pure function: takes a label, clearance, and compiled SPIF index,
/// returns PASS/FAIL. No side effects, no I/O, no caching.
/// This is the hot path — must be allocation-free on the happy path.
/// </summary>
public interface IAcdfEvaluator
{
    /// <summary>
    /// Evaluate whether the given clearance dominates the given label
    /// per the ACDF algorithm defined by xmlspif.org and SDN.801c.
    /// </summary>
    AcdfResult Evaluate(
        in SecurityLabel label,
        in SecurityClearance clearance,
        ISpifIndex spifIndex);

    /// <summary>
    /// Evaluate with step-by-step trace for explain mode.
    /// </summary>
    AcdfResult EvaluateWithTrace(
        in SecurityLabel label,
        in SecurityClearance clearance,
        ISpifIndex spifIndex,
        AcdfTraceCollector trace);
}

/// <summary>
/// Collector for ACDF evaluation trace steps.
/// </summary>
public class AcdfTraceCollector
{
    private readonly List<TraceStep> _steps = [];

    /// <summary>Record a trace step.</summary>
    public void AddStep(string ruleId, string effect, bool result, string reason)
    {
        _steps.Add(new TraceStep
        {
            RuleId = ruleId,
            Effect = effect,
            Result = result,
            Reason = reason
        });
    }

    /// <summary>Get all recorded steps.</summary>
    public IReadOnlyList<TraceStep> GetSteps() => _steps.AsReadOnly();
}

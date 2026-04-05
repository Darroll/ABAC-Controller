using System.Globalization;
using System.Text;
using AbacController.Core.Constants;

namespace AbacController.Api.Observability;

/// <summary>
/// Minimal in-process metrics sink with Prometheus text exposition.
/// </summary>
public sealed class ApiMetrics
{
    private long _totalEvaluations;
    private long _permitEvaluations;
    private long _denyEvaluations;
    private long _notApplicableEvaluations;
    private long _indeterminateEvaluations;
    private long _batchEvaluations;
    private long _evaluationDurationCount;
    private double _evaluationDurationTotalMs;
    private long _cacheHits;
    private long _cacheMisses;
    private long _cacheBypass;
    private long _pipResolutions;
    private long _pipFailures;
    private long _policyChangeEvents;
    private long _simulationEvaluations;

    /// <summary>Records a single authorization evaluation and its duration.</summary>
    public void RecordEvaluation(Decision decision, TimeSpan evaluationTime)
    {
        Interlocked.Increment(ref _totalEvaluations);
        Interlocked.Increment(ref _evaluationDurationCount);
        AddDouble(ref _evaluationDurationTotalMs, evaluationTime.TotalMilliseconds);

        switch (decision)
        {
            case Decision.Permit:
                Interlocked.Increment(ref _permitEvaluations);
                break;
            case Decision.Deny:
                Interlocked.Increment(ref _denyEvaluations);
                break;
            case Decision.NotApplicable:
                Interlocked.Increment(ref _notApplicableEvaluations);
                break;
            case Decision.Indeterminate:
                Interlocked.Increment(ref _indeterminateEvaluations);
                break;
        }
    }

    /// <summary>Records the number of members processed within a batch evaluation request.</summary>
    public void RecordBatchEvaluation(int count)
    {
        Interlocked.Add(ref _batchEvaluations, count);
    }

    /// <summary>Records a decision-cache hit.</summary>
    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);

    /// <summary>Records a decision-cache miss.</summary>
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    /// <summary>Records an evaluation that explicitly bypassed the decision cache.</summary>
    public void RecordCacheBypass() => Interlocked.Increment(ref _cacheBypass);

    /// <summary>Records a successful PIP attribute resolution attempt.</summary>
    public void RecordPipResolution() => Interlocked.Increment(ref _pipResolutions);

    /// <summary>Records a failed PIP attribute resolution attempt.</summary>
    public void RecordPipFailure() => Interlocked.Increment(ref _pipFailures);

    /// <summary>Records a policy administration change event.</summary>
    public void RecordPolicyChange() => Interlocked.Increment(ref _policyChangeEvents);

    /// <summary>Records a simulation or dry-run evaluation.</summary>
    public void RecordSimulation() => Interlocked.Increment(ref _simulationEvaluations);

    /// <summary>Renders the current in-memory metrics snapshot using Prometheus text exposition format.</summary>
    public string RenderPrometheus(AppRuntimeStateSnapshot snapshot)
    {
        var builder = new StringBuilder();
        AppendCounter(builder, "abac_evaluations_total", "Total authorization evaluations.", Interlocked.Read(ref _totalEvaluations));
        AppendLabeledCounter(builder, "abac_evaluations_by_decision_total", "Authorization evaluations by decision.", "decision", "permit", Interlocked.Read(ref _permitEvaluations));
        AppendLabeledCounter(builder, "abac_evaluations_by_decision_total", "Authorization evaluations by decision.", "decision", "deny", Interlocked.Read(ref _denyEvaluations));
        AppendLabeledCounter(builder, "abac_evaluations_by_decision_total", "Authorization evaluations by decision.", "decision", "not_applicable", Interlocked.Read(ref _notApplicableEvaluations));
        AppendLabeledCounter(builder, "abac_evaluations_by_decision_total", "Authorization evaluations by decision.", "decision", "indeterminate", Interlocked.Read(ref _indeterminateEvaluations));
        AppendCounter(builder, "abac_batch_evaluations_total", "Total AuthZEN batch members evaluated.", Interlocked.Read(ref _batchEvaluations));
        AppendCounter(builder, "abac_evaluation_duration_ms_count", "Number of recorded evaluation durations.", Interlocked.Read(ref _evaluationDurationCount));
        AppendGauge(builder, "abac_evaluation_duration_ms_sum", "Sum of evaluation durations in milliseconds.", Interlocked.CompareExchange(ref _evaluationDurationTotalMs, 0, 0));
        AppendCounter(builder, "abac_cache_hits_total", "Decision cache hits.", Interlocked.Read(ref _cacheHits));
        AppendCounter(builder, "abac_cache_misses_total", "Decision cache misses.", Interlocked.Read(ref _cacheMisses));
        AppendCounter(builder, "abac_cache_bypass_total", "Decision cache bypasses.", Interlocked.Read(ref _cacheBypass));
        AppendCounter(builder, "abac_pip_resolutions_total", "PIP attribute resolutions.", Interlocked.Read(ref _pipResolutions));
        AppendCounter(builder, "abac_pip_failures_total", "PIP attribute resolution failures.", Interlocked.Read(ref _pipFailures));
        AppendCounter(builder, "abac_policy_changes_total", "Policy administration changes.", Interlocked.Read(ref _policyChangeEvents));
        AppendCounter(builder, "abac_simulation_evaluations_total", "Simulation/dry-run evaluations.", Interlocked.Read(ref _simulationEvaluations));
        AppendGauge(builder, "abac_startup_complete", "Startup completion state (1=complete).", snapshot.StartupCompleted ? 1 : 0);
        AppendGauge(builder, "abac_ready", "Readiness state (1=ready).", snapshot.Ready ? 1 : 0);
        return builder.ToString();
    }

    private static void AppendCounter(StringBuilder builder, string name, string help, long value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(" counter");
        builder.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendLabeledCounter(
        StringBuilder builder,
        string name,
        string help,
        string labelName,
        string labelValue,
        long value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(" counter");
        builder.Append(name)
            .Append('{').Append(labelName).Append("=\"").Append(labelValue).Append("\"} ")
            .AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendGauge(StringBuilder builder, string name, string help, double value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(" gauge");
        builder.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddDouble(ref double location, double value)
    {
        double initialValue;
        double computedValue;
        do
        {
            initialValue = location;
            computedValue = initialValue + value;
        }
        while (Interlocked.CompareExchange(ref location, computedValue, initialValue) != initialValue);
    }
}

/// <summary>
/// Captures the coarse runtime state exported alongside application metrics.
/// </summary>
public readonly record struct AppRuntimeStateSnapshot(bool StartupCompleted, bool Ready);

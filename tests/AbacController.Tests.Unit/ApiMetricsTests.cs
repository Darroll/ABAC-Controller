using AbacController.Api.Observability;
using AbacController.Core.Constants;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for ApiMetrics — counters, prometheus rendering.
/// </summary>
public sealed class ApiMetricsTests
{
    [Fact]
    public void RecordEvaluation_IncrementsTotalAndDecision()
    {
        var metrics = new ApiMetrics();
        metrics.RecordEvaluation(Decision.Permit, TimeSpan.FromMilliseconds(5));
        metrics.RecordEvaluation(Decision.Deny, TimeSpan.FromMilliseconds(10));

        var snapshot = new AppRuntimeStateSnapshot(true, true);
        var text = metrics.RenderPrometheus(snapshot);

        Assert.Contains("abac_evaluations_total 2", text);
        Assert.Contains("decision=\"permit\"} 1", text);
        Assert.Contains("decision=\"deny\"} 1", text);
    }

    [Fact]
    public void RecordBatchEvaluation_IncrementsCounter()
    {
        var metrics = new ApiMetrics();
        metrics.RecordBatchEvaluation(5);
        metrics.RecordBatchEvaluation(3);

        var snapshot = new AppRuntimeStateSnapshot(true, true);
        var text = metrics.RenderPrometheus(snapshot);

        Assert.Contains("abac_batch_evaluations_total 8", text);
    }

    [Fact]
    public void RecordCacheHitMiss_TrackedCorrectly()
    {
        var metrics = new ApiMetrics();
        metrics.RecordCacheHit();
        metrics.RecordCacheHit();
        metrics.RecordCacheMiss();

        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));

        Assert.Contains("abac_cache_hits_total 2", text);
        Assert.Contains("abac_cache_misses_total 1", text);
    }

    [Fact]
    public void RecordPipResolutionAndFailure_Tracked()
    {
        var metrics = new ApiMetrics();
        metrics.RecordPipResolution();
        metrics.RecordPipResolution();
        metrics.RecordPipFailure();

        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));

        Assert.Contains("abac_pip_resolutions_total 2", text);
        Assert.Contains("abac_pip_failures_total 1", text);
    }

    [Fact]
    public void RenderPrometheus_IncludesStartupAndReadyGauges()
    {
        var metrics = new ApiMetrics();
        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));

        Assert.Contains("abac_startup_complete 1", text);
        Assert.Contains("abac_ready 1", text);
    }

    [Fact]
    public void RenderPrometheus_NotReady_ShowsZero()
    {
        var metrics = new ApiMetrics();
        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(false, false));

        Assert.Contains("abac_startup_complete 0", text);
        Assert.Contains("abac_ready 0", text);
    }

    [Fact]
    public void RecordSimulation_Tracked()
    {
        var metrics = new ApiMetrics();
        metrics.RecordSimulation();
        metrics.RecordSimulation();

        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));
        Assert.Contains("abac_simulation_evaluations_total 2", text);
    }

    [Fact]
    public void RecordPolicyChange_Tracked()
    {
        var metrics = new ApiMetrics();
        metrics.RecordPolicyChange();

        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));
        Assert.Contains("abac_policy_changes_total 1", text);
    }

    [Fact]
    public void EvaluationDurationSum_AccumulatesCorrectly()
    {
        var metrics = new ApiMetrics();
        metrics.RecordEvaluation(Decision.Permit, TimeSpan.FromMilliseconds(10));
        metrics.RecordEvaluation(Decision.Deny, TimeSpan.FromMilliseconds(20));

        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));

        Assert.Contains("abac_evaluation_duration_ms_count 2", text);
        Assert.Contains("abac_evaluation_duration_ms_sum 30", text);
    }

    [Fact]
    public void AllDecisionTypes_Tracked()
    {
        var metrics = new ApiMetrics();
        metrics.RecordEvaluation(Decision.Permit, TimeSpan.Zero);
        metrics.RecordEvaluation(Decision.Deny, TimeSpan.Zero);
        metrics.RecordEvaluation(Decision.NotApplicable, TimeSpan.Zero);
        metrics.RecordEvaluation(Decision.Indeterminate, TimeSpan.Zero);

        var text = metrics.RenderPrometheus(new AppRuntimeStateSnapshot(true, true));

        Assert.Contains("decision=\"permit\"} 1", text);
        Assert.Contains("decision=\"deny\"} 1", text);
        Assert.Contains("decision=\"not_applicable\"} 1", text);
        Assert.Contains("decision=\"indeterminate\"} 1", text);
    }
}

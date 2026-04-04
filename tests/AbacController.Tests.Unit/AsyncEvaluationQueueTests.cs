using AbacController.Api;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for AsyncEvaluationQueue — enqueue, dequeue, completion, status tracking.
/// </summary>
public sealed class AsyncEvaluationQueueTests
{
    private static EvaluationRequest MakeRequest() => new()
    {
        Subject = new SubjectInfo { Type = "user", Id = "alice" },
        Action = new ActionInfo { Name = "read" },
        Resource = new ResourceInfo { Type = "document", Id = "doc-1" }
    };

    private static EvaluationResult MakeResult(string id) => new()
    {
        DecisionId = id,
        Decision = Decision.Permit
    };

    [Fact]
    public void Enqueue_SetsJobAsPending()
    {
        var queue = new AsyncEvaluationQueue();
        var job = new AsyncEvaluationJob("eval-1", MakeRequest(), "https://example.com/callback", new());

        queue.Enqueue(job);

        Assert.True(queue.IsQueued("eval-1"));
        Assert.False(queue.TryGetResult("eval-1", out _));
    }

    [Fact]
    public void Complete_MovesJobToCompleted()
    {
        var queue = new AsyncEvaluationQueue();
        var job = new AsyncEvaluationJob("eval-2", MakeRequest(), "https://example.com/callback", new());

        queue.Enqueue(job);
        queue.Complete("eval-2", MakeResult("eval-2"));

        Assert.False(queue.IsQueued("eval-2"));
        Assert.True(queue.TryGetResult("eval-2", out var result));
        Assert.Equal(Decision.Permit, result!.Decision);
    }

    [Fact]
    public async Task Enqueue_WritesToChannel()
    {
        var queue = new AsyncEvaluationQueue();
        var job = new AsyncEvaluationJob("eval-3", MakeRequest(), "https://example.com/callback", new());

        queue.Enqueue(job);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var dequeued = await queue.Reader.ReadAsync(cts.Token);

        Assert.Equal("eval-3", dequeued.EvaluationId);
    }

    [Fact]
    public void TryGetResult_UnknownId_ReturnsFalse()
    {
        var queue = new AsyncEvaluationQueue();
        Assert.False(queue.TryGetResult("nonexistent", out _));
    }

    [Fact]
    public void IsQueued_UnknownId_ReturnsFalse()
    {
        var queue = new AsyncEvaluationQueue();
        Assert.False(queue.IsQueued("nonexistent"));
    }

    [Fact]
    public void Complete_WithDenyDecision_StoresCorrectly()
    {
        var queue = new AsyncEvaluationQueue();
        var job = new AsyncEvaluationJob("eval-deny", MakeRequest(), "https://example.com/callback", new());

        queue.Enqueue(job);
        queue.Complete("eval-deny", new EvaluationResult
        {
            DecisionId = "eval-deny",
            Decision = Decision.Deny,
            Status = new StatusInfo { Code = "ok", Message = "Denied by policy" }
        });

        Assert.True(queue.TryGetResult("eval-deny", out var result));
        Assert.Equal(Decision.Deny, result!.Decision);
        Assert.Equal("Denied by policy", result.Status!.Message);
    }

    [Fact]
    public void Enqueue_MultipleJobs_AllTracked()
    {
        var queue = new AsyncEvaluationQueue();
        for (var i = 0; i < 5; i++)
        {
            queue.Enqueue(new AsyncEvaluationJob($"eval-{i}", MakeRequest(), "https://example.com/callback", new()));
        }

        for (var i = 0; i < 5; i++)
        {
            Assert.True(queue.IsQueued($"eval-{i}"));
        }
    }
}

using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;
using AbacController.Pip;

namespace AbacController.Tests.Unit;

public sealed class PipHealthMonitorTests
{
    [Fact]
    public async Task CheckAllAsync_ReturnsStatusForAllSources()
    {
        var sources = new IPipSource[]
        {
            new FakePipSource("source-1", "static", true),
            new FakePipSource("source-2", "rest", false)
        };

        var monitor = new PipHealthMonitor(sources: sources);
        var statuses = await monitor.CheckAllAsync();

        Assert.Equal(2, statuses.Count);
        Assert.True(statuses.First(s => s.SourceId == "source-1").Healthy);
        Assert.False(statuses.First(s => s.SourceId == "source-2").Healthy);
    }

    [Fact]
    public async Task GetStatuses_ReturnsCachedResults()
    {
        var sources = new IPipSource[] { new FakePipSource("src", "static", true) };
        var monitor = new PipHealthMonitor(sources: sources);

        // Initially empty
        Assert.Empty(monitor.GetStatuses());

        // After check, cached
        await monitor.CheckAllAsync();
        var cached = monitor.GetStatuses();
        Assert.Single(cached);
        Assert.True(cached[0].Healthy);
    }

    [Fact]
    public async Task GetStatus_ReturnsSpecificSource()
    {
        var sources = new IPipSource[]
        {
            new FakePipSource("src-a", "static", true),
            new FakePipSource("src-b", "rest", false)
        };

        var monitor = new PipHealthMonitor(sources: sources);
        await monitor.CheckAllAsync();

        var status = monitor.GetStatus("src-a");
        Assert.NotNull(status);
        Assert.True(status.Healthy);

        Assert.Null(monitor.GetStatus("nonexistent"));
    }

    private sealed class FakePipSource : IPipSource
    {
        private readonly bool _healthy;

        public string SourceType { get; }
        public string SourceId { get; }
        public IReadOnlySet<string> ProvidesAttributes { get; } = new HashSet<string> { "test" };
        public int Priority => 10;

        public FakePipSource(string sourceId, string sourceType, bool healthy)
        {
            SourceId = sourceId;
            SourceType = sourceType;
            _healthy = healthy;
        }

        public Task<AttributeResolutionResult> ResolveAsync(
            AttributeResolutionRequest request, CancellationToken ct = default)
            => Task.FromResult(AttributeResolutionResult.Succeeded([]));

        public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
            => Task.FromResult(new SourceHealthResult
            {
                Healthy = _healthy,
                Message = _healthy ? "OK" : "Down",
                ResponseTime = TimeSpan.FromMilliseconds(10)
            });
    }
}

using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;
using AbacController.Pip;

namespace AbacController.Tests.Unit;

public sealed class PipResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsCachedValue_WhenCacheHitExists()
    {
        var cache = new PipCacheManager();
        var value = new AttributeValue
        {
            Name = "department",
            Category = AttributeCategory.Subject,
            Value = "engineering",
            SourceId = "static-1",
            SourceType = "static",
            FetchedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(5)
        };
        cache.Set("static-1", "user-1", value, TimeSpan.FromMinutes(5));

        var source = new TestPipSource("static-1", 1, new HashSet<string> { "department" }, _ => throw new Xunit.Sdk.XunitException("Should not be called"));
        var resolver = new PipResolver([source], cache);

        var result = await resolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "user-1",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.True(result.Success);
        Assert.Single(result.Values);
        Assert.True(result.Values[0].FromCache);
        Assert.Equal("engineering", result.Values[0].Value);
    }

    [Fact]
    public async Task ResolveAsync_FetchesFromSource_WhenCacheMissOccurs()
    {
        var source = new TestPipSource("source-1", 1, new HashSet<string> { "department" }, request =>
            Task.FromResult(AttributeResolutionResult.Succeeded([
                new AttributeValue
                {
                    Name = "department",
                    Category = AttributeCategory.Subject,
                    Value = "engineering",
                    SourceId = "source-1",
                    SourceType = "test",
                    FetchedAt = DateTimeOffset.UtcNow,
                    CacheTtl = TimeSpan.FromMinutes(5)
                }
            ])));

        var resolver = new PipResolver([source], new PipCacheManager());

        var result = await resolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "user-1",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.True(result.Success);
        Assert.Single(result.Values);
        Assert.False(result.Values[0].FromCache);
        Assert.Empty(result.Missing);
        Assert.Equal(1, source.ResolveCalls);
    }

    [Fact]
    public async Task ResolveAsync_UsesPriorityOrder_AndStopsWhenSatisfied()
    {
        var first = new TestPipSource("first", 1, new HashSet<string> { "department" }, request =>
            Task.FromResult(AttributeResolutionResult.Succeeded([
                new AttributeValue
                {
                    Name = "department",
                    Category = AttributeCategory.Subject,
                    Value = "engineering",
                    SourceId = "first",
                    SourceType = "test",
                    FetchedAt = DateTimeOffset.UtcNow
                }
            ])));
        var second = new TestPipSource("second", 2, new HashSet<string> { "department" }, _ => throw new Xunit.Sdk.XunitException("Should not be called"));

        var resolver = new PipResolver([second, first], new PipCacheManager());
        var result = await resolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "user-1",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.True(result.Success);
        Assert.Equal(1, first.ResolveCalls);
        Assert.Equal(0, second.ResolveCalls);
    }

    [Fact]
    public async Task ResolveAsync_SourceThrows_FallsBackToNextSource()
    {
        var broken = new TestPipSource("broken", 1, new HashSet<string> { "department" }, _ => throw new InvalidOperationException("boom"));
        var fallback = new TestPipSource("fallback", 2, new HashSet<string> { "department" }, request =>
            Task.FromResult(AttributeResolutionResult.Succeeded([
                new AttributeValue
                {
                    Name = "department",
                    Category = AttributeCategory.Subject,
                    Value = "engineering",
                    SourceId = "fallback",
                    SourceType = "test",
                    FetchedAt = DateTimeOffset.UtcNow
                }
            ])));

        var resolver = new PipResolver([broken, fallback], new PipCacheManager());
        var result = await resolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "user-1",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.True(result.Success);
        Assert.Equal(1, broken.ResolveCalls);
        Assert.Equal(1, fallback.ResolveCalls);
        Assert.Equal("engineering", result.Values.Single().Value);
    }

    [Fact]
    public async Task ResolveAsync_UnresolvedAttributes_AppearInMissingList()
    {
        var source = new TestPipSource("source-1", 1, new HashSet<string> { "department" }, _ =>
            Task.FromResult(AttributeResolutionResult.Succeeded([])));

        var resolver = new PipResolver([source], new PipCacheManager());
        var result = await resolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "user-1",
            SubjectType = "user",
            RequestedAttributes = ["department", "clearance"]
        });

        Assert.False(result.Success);
        Assert.Contains("department", result.Missing);
        Assert.Contains("clearance", result.Missing);
    }

    private sealed class TestPipSource : IPipSource
    {
        private readonly Func<AttributeResolutionRequest, Task<AttributeResolutionResult>> _handler;

        public TestPipSource(string sourceId, int priority, IReadOnlySet<string> providesAttributes, Func<AttributeResolutionRequest, Task<AttributeResolutionResult>> handler)
        {
            SourceId = sourceId;
            Priority = priority;
            ProvidesAttributes = providesAttributes;
            _handler = handler;
        }

        public string SourceType => "test";
        public string SourceId { get; }
        public IReadOnlySet<string> ProvidesAttributes { get; }
        public int Priority { get; }
        public int ResolveCalls { get; private set; }

        public Task<AttributeResolutionResult> ResolveAsync(AttributeResolutionRequest request, CancellationToken ct = default)
        {
            ResolveCalls++;
            return _handler(request);
        }

        public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
            => Task.FromResult(new SourceHealthResult { Healthy = true });
    }
}

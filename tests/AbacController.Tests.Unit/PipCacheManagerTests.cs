using AbacController.Core.Domain.Attributes;
using AbacController.Pip;

namespace AbacController.Tests.Unit;

public sealed class PipCacheManagerTests
{
    private static AttributeValue CreateValue(string name = "department", object? value = null) => new()
    {
        Name = name,
        Category = AttributeCategory.Subject,
        Value = value ?? "engineering",
        SourceId = "source-1",
        SourceType = "test",
        FetchedAt = DateTimeOffset.UtcNow,
        CacheTtl = TimeSpan.FromMinutes(1)
    };

    [Fact]
    public void Set_ThenTryGet_ReturnsValue()
    {
        var cache = new PipCacheManager();
        var input = CreateValue();
        cache.Set("source-1", "user-1", input, TimeSpan.FromMinutes(1));

        var found = cache.TryGet("source-1", "user-1", "department", out var output);

        Assert.True(found);
        Assert.NotNull(output);
        Assert.Equal("engineering", output!.Value);
    }

    [Fact]
    public void TryGet_MissingValue_ReturnsFalse()
    {
        var cache = new PipCacheManager();
        Assert.False(cache.TryGet("source-1", "user-1", "department", out _));
    }

    [Fact]
    public void Invalidate_RemovesOnlySpecifiedSubject()
    {
        var cache = new PipCacheManager();
        cache.Set("source-1", "user-1", CreateValue(), TimeSpan.FromMinutes(1));
        cache.Set("source-1", "user-2", CreateValue(), TimeSpan.FromMinutes(1));

        cache.Invalidate("user-1");

        Assert.False(cache.TryGet("source-1", "user-1", "department", out _));
        Assert.True(cache.TryGet("source-1", "user-2", "department", out _));
    }

    [Fact]
    public void InvalidateAll_RemovesAllSubjects()
    {
        var cache = new PipCacheManager();
        cache.Set("source-1", "user-1", CreateValue(), TimeSpan.FromMinutes(1));
        cache.Set("source-1", "user-2", CreateValue(), TimeSpan.FromMinutes(1));

        cache.InvalidateAll();

        Assert.False(cache.TryGet("source-1", "user-1", "department", out _));
        Assert.False(cache.TryGet("source-1", "user-2", "department", out _));
    }

    [Fact]
    public void Invalidate_MissingSubject_NoOp()
    {
        var cache = new PipCacheManager();
        cache.Invalidate("missing");
    }
}

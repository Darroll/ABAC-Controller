using AbacController.Pap;

namespace AbacController.Tests.Unit;

/// <summary>
/// Edge case tests for PolicyVersionDiff — empty, identical, large diffs.
/// </summary>
public sealed class PolicyVersionDiffEdgeCaseTests
{
    [Fact]
    public void Compare_IdenticalContent_NoChanges()
    {
        var content = "{ \"effect\": \"permit\", \"target\": \"all\" }";
        var result = PolicyVersionDiff.Compare(content, content);

        Assert.False(result.HasChanges);
        Assert.All(result.Operations, op => Assert.Equal("equal", op.Operation));
    }

    [Fact]
    public void Compare_EmptyToContent_HasChanges()
    {
        var result = PolicyVersionDiff.Compare("", "line1\nline2\nline3");

        Assert.True(result.HasChanges);
        Assert.True(result.NewLineCount > 0);
    }

    [Fact]
    public void Compare_ContentToEmpty_HasChanges()
    {
        var result = PolicyVersionDiff.Compare("line1\nline2\nline3", "");

        Assert.True(result.HasChanges);
        Assert.True(result.OldLineCount > 0);
    }

    [Fact]
    public void Compare_SingleLineChange_DetectsIt()
    {
        var left = "line1\nline2\nline3";
        var right = "line1\nline2-modified\nline3";

        var result = PolicyVersionDiff.Compare(left, right);

        Assert.True(result.HasChanges);
        Assert.True(result.Operations.Count > 0);
    }

    [Fact]
    public void Compare_MultipleChanges_AllDetected()
    {
        var left = "a\nb\nc\nd\ne";
        var right = "a\nB\nc\nD\ne";

        var result = PolicyVersionDiff.Compare(left, right);

        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Compare_BothEmpty_NoChanges()
    {
        var result = PolicyVersionDiff.Compare("", "");
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Compare_LineCountsCorrect()
    {
        var result = PolicyVersionDiff.Compare("a\nb\nc", "x\ny");
        Assert.Equal(3, result.OldLineCount);
        Assert.Equal(2, result.NewLineCount);
    }
}

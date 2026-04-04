using AbacController.Pap;

namespace AbacController.Tests.Unit;

public sealed class PolicyVersionDiffTests
{
    [Fact]
    public void Compare_IdenticalContent_NoChanges()
    {
        var content = "{\n  \"effect\": \"Permit\"\n}";
        var diff = PolicyVersionDiff.Compare(content, content);

        Assert.False(diff.HasChanges);
        Assert.All(diff.Operations, op => Assert.Equal("equal", op.Operation));
    }

    [Fact]
    public void Compare_DetectsInsertedLines()
    {
        var oldContent = "line1\nline3";
        var newContent = "line1\nline2\nline3";

        var diff = PolicyVersionDiff.Compare(oldContent, newContent);

        Assert.True(diff.HasChanges);
        Assert.Contains(diff.Operations, op => op.Operation == "insert" && op.Content == "line2");
    }

    [Fact]
    public void Compare_DetectsDeletedLines()
    {
        var oldContent = "line1\nline2\nline3";
        var newContent = "line1\nline3";

        var diff = PolicyVersionDiff.Compare(oldContent, newContent);

        Assert.True(diff.HasChanges);
        Assert.Contains(diff.Operations, op => op.Operation == "delete" && op.Content == "line2");
    }

    [Fact]
    public void Compare_DetectsChangedLines_AsDeletePlusInsert()
    {
        var oldContent = "{\n  \"effect\": \"Permit\"\n}";
        var newContent = "{\n  \"effect\": \"Deny\"\n}";

        var diff = PolicyVersionDiff.Compare(oldContent, newContent);

        Assert.True(diff.HasChanges);
        Assert.Contains(diff.Operations, op => op.Operation == "delete" && op.Content.Contains("Permit", StringComparison.Ordinal));
        Assert.Contains(diff.Operations, op => op.Operation == "insert" && op.Content.Contains("Deny", StringComparison.Ordinal));
    }
}

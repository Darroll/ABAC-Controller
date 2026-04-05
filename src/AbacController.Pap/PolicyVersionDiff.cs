namespace AbacController.Pap;

/// <summary>
/// Computes readable line-by-line diffs between policy versions.
/// Lightweight and deterministic for API/UI consumption.
/// </summary>
public static class PolicyVersionDiff
{
    /// <summary>
    /// Executes compare.
    /// </summary>
    public static PolicyDiffResult Compare(string oldContent, string newContent)
    {
        var oldLines = NormalizeLines(oldContent);
        var newLines = NormalizeLines(newContent);
        var operations = BuildDiff(oldLines, newLines);

        return new PolicyDiffResult
        {
            OldLineCount = oldLines.Length,
            NewLineCount = newLines.Length,
            HasChanges = operations.Any(static op => op.Operation != "equal"),
            Operations = operations
        };
    }

    private static string[] NormalizeLines(string content)
        => (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

    private static List<PolicyDiffOperation> BuildDiff(string[] oldLines, string[] newLines)
    {
        var lcs = BuildLcsTable(oldLines, newLines);
        var operations = new List<PolicyDiffOperation>();

        var i = 0;
        var j = 0;
        while (i < oldLines.Length && j < newLines.Length)
        {
            if (string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal))
            {
                operations.Add(new PolicyDiffOperation
                {
                    Operation = "equal",
                    OldLineNumber = i + 1,
                    NewLineNumber = j + 1,
                    Content = oldLines[i]
                });
                i++;
                j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                operations.Add(new PolicyDiffOperation
                {
                    Operation = "delete",
                    OldLineNumber = i + 1,
                    Content = oldLines[i]
                });
                i++;
            }
            else
            {
                operations.Add(new PolicyDiffOperation
                {
                    Operation = "insert",
                    NewLineNumber = j + 1,
                    Content = newLines[j]
                });
                j++;
            }
        }

        while (i < oldLines.Length)
        {
            operations.Add(new PolicyDiffOperation
            {
                Operation = "delete",
                OldLineNumber = i + 1,
                Content = oldLines[i]
            });
            i++;
        }

        while (j < newLines.Length)
        {
            operations.Add(new PolicyDiffOperation
            {
                Operation = "insert",
                NewLineNumber = j + 1,
                Content = newLines[j]
            });
            j++;
        }

        return operations;
    }

    private static int[,] BuildLcsTable(string[] oldLines, string[] newLines)
    {
        var table = new int[oldLines.Length + 1, newLines.Length + 1];

        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        return table;
    }
}

/// <summary>
/// A PolicyDiffResult record.
/// </summary>
public sealed record PolicyDiffResult
{
    /// <summary>Gets or sets the old Line Count.</summary>
    public required int OldLineCount { get; init; }
    /// <summary>Gets or sets the new Line Count.</summary>
    public required int NewLineCount { get; init; }
    /// <summary>Gets or sets the has Changes.</summary>
    public required bool HasChanges { get; init; }
    /// <summary>Gets or sets the operations.</summary>
    public required List<PolicyDiffOperation> Operations { get; init; }
}

/// <summary>
/// A PolicyDiffOperation record.
/// </summary>
public sealed record PolicyDiffOperation
{
    /// <summary>Gets or sets the operation.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets or sets the old Line Number.</summary>
    public int? OldLineNumber { get; init; }
    /// <summary>Gets or sets the new Line Number.</summary>
    public int? NewLineNumber { get; init; }
    /// <summary>Gets or sets the content.</summary>
    public required string Content { get; init; }
}

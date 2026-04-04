using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip.Sources;

/// <summary>
/// File-based PIP source. Loads attribute data from CSV or JSON files.
/// Supports hot-reloading when the file changes.
/// 
/// JSON format: { "subjectId": { "attr1": "val1", "attr2": "val2" }, ... }
/// CSV format: subjectId,attr1,attr2,... (first row = header, first column = subject ID)
/// </summary>
public sealed class FilePipSource : IPipSource
{
    private readonly string _filePath;
    private readonly TimeSpan _cacheTtl;
    private readonly object _lock = new();
    private Dictionary<string, Dictionary<string, object>> _data = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastModified;
    private HashSet<string> _providesAttributes = new(StringComparer.Ordinal);

    public string SourceType => "file";
    public string SourceId { get; }
    public IReadOnlySet<string> ProvidesAttributes
    {
        get
        {
            EnsureLoaded();
            lock (_lock) return _providesAttributes;
        }
    }
    public int Priority { get; }

    public FilePipSource(string sourceId, int priority, string filePath, TimeSpan? cacheTtl = null)
    {
        SourceId = sourceId;
        Priority = priority;
        _filePath = filePath;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(600);
    }

    public Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request, CancellationToken ct = default)
    {
        EnsureLoaded();

        Dictionary<string, Dictionary<string, object>> snapshot;
        lock (_lock) snapshot = _data;

        var values = new List<AttributeValue>();

        if (snapshot.TryGetValue(request.SubjectId, out var subjectAttrs))
        {
            foreach (var attrName in request.RequestedAttributes)
            {
                if (subjectAttrs.TryGetValue(attrName, out var value))
                {
                    values.Add(new AttributeValue
                    {
                        Name = attrName,
                        Category = AttributeCategory.Subject,
                        Value = value,
                        SourceId = SourceId,
                        SourceType = SourceType,
                        FetchedAt = DateTimeOffset.UtcNow,
                        CacheTtl = _cacheTtl
                    });
                }
            }
        }

        return Task.FromResult(AttributeResolutionResult.Succeeded(values));
    }

    public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var exists = File.Exists(_filePath);
            sw.Stop();
            return Task.FromResult(new SourceHealthResult
            {
                Healthy = exists,
                Message = exists ? $"File exists ({new FileInfo(_filePath).Length} bytes)" : $"File not found: {_filePath}",
                ResponseTime = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new SourceHealthResult
            {
                Healthy = false,
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            });
        }
    }

    private void EnsureLoaded()
    {
        if (!File.Exists(_filePath))
            return;

        var lastWrite = File.GetLastWriteTimeUtc(_filePath);
        if (lastWrite <= _lastModified)
            return;

        lock (_lock)
        {
            // Double-check after lock
            lastWrite = File.GetLastWriteTimeUtc(_filePath);
            if (lastWrite <= _lastModified)
                return;

            try
            {
                var extension = Path.GetExtension(_filePath).ToLowerInvariant();
                _data = extension switch
                {
                    ".json" => LoadJson(_filePath),
                    ".csv" => LoadCsv(_filePath),
                    _ => throw new NotSupportedException($"Unsupported file extension: {extension}")
                };

                _providesAttributes = _data.Values
                    .SelectMany(static v => v.Keys)
                    .ToHashSet(StringComparer.Ordinal);

                _lastModified = lastWrite;
            }
            catch
            {
                // Keep existing data on parse failure
            }
        }
    }

    private static Dictionary<string, Dictionary<string, object>> LoadJson(string path)
    {
        var content = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(content);
        var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        foreach (var subjectProp in doc.RootElement.EnumerateObject())
        {
            if (subjectProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            var attrs = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var attrProp in subjectProp.Value.EnumerateObject())
            {
                attrs[attrProp.Name] = attrProp.Value.ValueKind switch
                {
                    JsonValueKind.String => attrProp.Value.GetString()!,
                    JsonValueKind.Number when attrProp.Value.TryGetInt64(out var longVal) => longVal,
                    JsonValueKind.Number => attrProp.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => attrProp.Value.GetRawText()
                };
            }

            result[subjectProp.Name] = attrs;
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object>> LoadCsv(string path)
    {
        var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path);

        if (lines.Length < 2)
            return result;

        var headers = ParseCsvLine(lines[0]);

        for (var i = 1; i < lines.Length; i++)
        {
            var fields = ParseCsvLine(lines[i]);
            if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0]))
                continue;

            var subjectId = fields[0].Trim();
            var attrs = new Dictionary<string, object>(StringComparer.Ordinal);

            for (var j = 1; j < Math.Min(headers.Length, fields.Length); j++)
            {
                var header = headers[j].Trim();
                var value = fields[j].Trim();

                if (!string.IsNullOrEmpty(header) && !string.IsNullOrEmpty(value))
                {
                    attrs[header] = ParseCsvValue(value);
                }
            }

            result[subjectId] = attrs;
        }

        return result;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static object ParseCsvValue(string value)
    {
        if (bool.TryParse(value, out var boolVal))
            return boolVal;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            return longVal;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
            return doubleVal;
        return value;
    }
}

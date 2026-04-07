using System.Collections.Immutable;
using System.Text.Json;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;

namespace AbacController.Api;

/// <summary>
/// Shared utility for parsing SecurityClearance and SecurityLabel from JSON properties.
/// Extracted from AuthZenController to avoid duplication across controllers.
/// </summary>
public static class SecurityModelParser
{
    /// <summary>Attempts to parse a <see cref="SecurityClearance"/> from subject properties.</summary>
    public static bool TryParseSecurityClearance(
        IReadOnlyDictionary<string, object?> properties,
        out SecurityClearance clearance)
    {
        clearance = null!;
        if (!properties.TryGetValue("securityClearance", out var raw) || raw is null)
            return false;

        if (raw is SecurityClearance existing)
        {
            clearance = existing;
            return true;
        }

        var element = ToJsonElement(raw);
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("policyOid", out var policyOidElement) ||
            string.IsNullOrWhiteSpace(policyOidElement.GetString()))
            return false;

        var classificationLacvs = element.TryGetProperty("classificationLacvs", out var classificationElement) && classificationElement.ValueKind == JsonValueKind.Array
            ? classificationElement.EnumerateArray()
                .Where(static entry => entry.TryGetInt32(out _))
                .Select(static entry => (LacvValue)entry.GetInt32())
                .ToImmutableHashSet()
            : ImmutableHashSet<LacvValue>.Empty;

        var categoryTagSets = new List<ClearanceCategoryTagSet>();
        if (element.TryGetProperty("categoryTagSets", out var categoryTagSetsElement) && categoryTagSetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagSetElement in categoryTagSetsElement.EnumerateArray())
            {
                if (!tagSetElement.TryGetProperty("tagSetOid", out var tagSetOidElement) || string.IsNullOrWhiteSpace(tagSetOidElement.GetString()))
                    continue;

                var tags = new List<ClearanceCategoryTag>();
                if (tagSetElement.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tagElement in tagsElement.EnumerateArray())
                    {
                        tags.Add(new ClearanceCategoryTag
                        {
                            TagOid = tagElement.TryGetProperty("tagOid", out var tagOidElement) ? tagOidElement.GetString() : null,
                            TagType = ParseTagType(tagElement.TryGetProperty("tagType", out var tagTypeElement) ? tagTypeElement.GetString() : null),
                            Bits = ParseLacvSet(tagElement, "bits"),
                            EnumeratedValues = ParseLacvSet(tagElement, "enumeratedValues")
                        });
                    }
                }

                categoryTagSets.Add(new ClearanceCategoryTagSet
                {
                    TagSetOid = tagSetOidElement.GetString()!,
                    Tags = tags.ToImmutableList()
                });
            }
        }

        clearance = new SecurityClearance
        {
            PolicyOid = policyOidElement.GetString()!,
            ClassificationLacvs = classificationLacvs,
            CategoryTagSets = categoryTagSets.ToImmutableList()
        };

        return true;
    }

    /// <summary>Parses an array of LACV integers from a JSON property.</summary>
    public static ImmutableHashSet<LacvValue> ParseLacvSet(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valuesElement) || valuesElement.ValueKind != JsonValueKind.Array)
            return ImmutableHashSet<LacvValue>.Empty;

        return valuesElement.EnumerateArray()
            .Where(static value => value.TryGetInt32(out _))
            .Select(static value => (LacvValue)value.GetInt32())
            .ToImmutableHashSet();
    }

    /// <summary>Parses a tag type string, defaulting to <see cref="TagType.Restrictive"/>.</summary>
    public static TagType ParseTagType(string? value)
        => Enum.TryParse<TagType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : TagType.Restrictive;

    /// <summary>Converts an arbitrary object to a <see cref="JsonElement"/>.</summary>
    public static JsonElement ToJsonElement(object raw)
    {
        if (raw is JsonElement element)
            return element;

        var json = JsonSerializer.Serialize(raw);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

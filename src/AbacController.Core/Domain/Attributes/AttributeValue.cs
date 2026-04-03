namespace AbacController.Core.Domain.Attributes;

/// <summary>
/// A resolved attribute value with provenance metadata.
/// </summary>
public sealed record AttributeValue
{
    /// <summary>Attribute name (e.g., "clearance", "department").</summary>
    public required string Name { get; init; }

    /// <summary>Attribute category.</summary>
    public required AttributeCategory Category { get; init; }

    /// <summary>The attribute value.</summary>
    public required object Value { get; init; }

    /// <summary>Source that provided this value.</summary>
    public required string SourceId { get; init; }

    /// <summary>Source type (e.g., "ldap", "oidc").</summary>
    public required string SourceType { get; init; }

    /// <summary>When this value was fetched.</summary>
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>TTL from the source configuration.</summary>
    public TimeSpan? CacheTtl { get; init; }

    /// <summary>Whether this came from cache.</summary>
    public bool FromCache { get; init; }
}

/// <summary>
/// Attribute categories per NIST SP 800-162.
/// </summary>
public enum AttributeCategory
{
    /// <summary>Attributes of the entity requesting access.</summary>
    Subject,

    /// <summary>Attributes of the resource being accessed.</summary>
    Resource,

    /// <summary>Attributes of the action being performed.</summary>
    Action,

    /// <summary>Environmental attributes (time, location, etc.).</summary>
    Environment
}

/// <summary>
/// Request for attribute resolution.
/// </summary>
public sealed record AttributeResolutionRequest
{
    /// <summary>Subject identifier.</summary>
    public required string SubjectId { get; init; }

    /// <summary>Subject type.</summary>
    public required string SubjectType { get; init; }

    /// <summary>Attribute names to resolve.</summary>
    public required IReadOnlyList<string> RequestedAttributes { get; init; }

    /// <summary>Additional context for resolution.</summary>
    public Dictionary<string, object?> Context { get; init; } = new();
}

/// <summary>
/// Result of attribute resolution from a PIP source.
/// </summary>
public sealed record AttributeResolutionResult
{
    /// <summary>Whether resolution was successful.</summary>
    public required bool Success { get; init; }

    /// <summary>Resolved attribute values.</summary>
    public List<AttributeValue> Values { get; init; } = [];

    /// <summary>Attributes that could not be resolved.</summary>
    public List<string> Missing { get; init; } = [];

    /// <summary>Error message if resolution failed.</summary>
    public string? Error { get; init; }

    /// <summary>Create a successful result.</summary>
    public static AttributeResolutionResult Succeeded(List<AttributeValue> values)
        => new() { Success = true, Values = values };

    /// <summary>Create a failed result.</summary>
    public static AttributeResolutionResult Failed(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// Health check result for a PIP source.
/// </summary>
public sealed record SourceHealthResult
{
    public required bool Healthy { get; init; }
    public string? Message { get; init; }
    public TimeSpan? ResponseTime { get; init; }
}

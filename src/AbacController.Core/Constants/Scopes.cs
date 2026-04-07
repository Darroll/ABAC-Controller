namespace AbacController.Core.Constants;

/// <summary>
/// OAuth 2.0 scope constants for ABAC Controller API authorization.
/// </summary>
public static class Scopes
{
    /// <summary>Scope for policy evaluation requests.</summary>
    public const string Evaluate = "abac:evaluate";

    /// <summary>Scope for policy evaluation with detailed decision explanations.</summary>
    public const string EvaluateExplain = "abac:evaluate:explain";

    /// <summary>Scope for reading policy definitions.</summary>
    public const string PolicyRead = "abac:policy:read";

    /// <summary>Scope for creating and updating policy definitions.</summary>
    public const string PolicyWrite = "abac:policy:write";

    /// <summary>Scope for full policy administration including deletion and versioning.</summary>
    public const string PolicyAdmin = "abac:policy:admin";

    /// <summary>Scope for reading PIP source configurations.</summary>
    public const string PipRead = "abac:pip:read";

    /// <summary>Scope for administering PIP source configurations.</summary>
    public const string PipAdmin = "abac:pip:admin";

    /// <summary>Scope for reading PEP enforcement point registrations.</summary>
    public const string PepRead = "abac:pep:read";

    /// <summary>Scope for administering PEP enforcement point registrations.</summary>
    public const string PepAdmin = "abac:pep:admin";

    /// <summary>Scope for label encoding and decoding operations.</summary>
    public const string PepLabel = "abac:pep:label";

    /// <summary>Scope for reading audit log entries.</summary>
    public const string AuditRead = "abac:audit:read";

    /// <summary>Scope for reading system status and diagnostics.</summary>
    public const string SysRead = "abac:sys:read";

    /// <summary>Scope for system administration operations.</summary>
    public const string SysAdmin = "abac:sys:admin";

    /// <summary>Scope for querying allowed classifications.</summary>
    public const string ClassificationQuery = "abac:classification:query";

    /// <summary>Scope for administering application registrations.</summary>
    public const string ApplicationAdmin = "abac:application:admin";
}

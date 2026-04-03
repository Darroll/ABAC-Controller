namespace AbacController.Core.Constants;

/// <summary>
/// OAuth 2.0 scope constants for ABAC Controller API authorization.
/// </summary>
public static class Scopes
{
    public const string Evaluate = "abac:evaluate";
    public const string EvaluateExplain = "abac:evaluate:explain";
    public const string PolicyRead = "abac:policy:read";
    public const string PolicyWrite = "abac:policy:write";
    public const string PolicyAdmin = "abac:policy:admin";
    public const string PipRead = "abac:pip:read";
    public const string PipAdmin = "abac:pip:admin";
    public const string PepRead = "abac:pep:read";
    public const string PepAdmin = "abac:pep:admin";
    public const string PepLabel = "abac:pep:label";
    public const string AuditRead = "abac:audit:read";
    public const string SysRead = "abac:sys:read";
    public const string SysAdmin = "abac:sys:admin";
}

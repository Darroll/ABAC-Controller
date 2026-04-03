namespace AbacController.Core.Domain.Decisions;

/// <summary>
/// Result of an ACDF (Access Control Decision Function) evaluation.
/// Binary PASS/FAIL with failure detail.
/// </summary>
public readonly record struct AcdfResult(
    bool Pass,
    AcdfFailureReason? FailureReason = null,
    string? FailureDetail = null)
{
    /// <summary>Create a passing result.</summary>
    public static AcdfResult Passed() => new(true);

    /// <summary>Create a failing result with reason.</summary>
    public static AcdfResult Fail(AcdfFailureReason reason, string detail)
        => new(false, reason, detail);
}

/// <summary>
/// Reason codes for ACDF evaluation failure.
/// </summary>
public enum AcdfFailureReason
{
    /// <summary>Label and clearance reference incompatible policies.</summary>
    PolicyMismatch,

    /// <summary>Clearance classification does not dominate label classification.</summary>
    ClassificationDominanceFailed,

    /// <summary>Restrictive category check failed (AND semantics).</summary>
    RestrictiveCategoryFailed,

    /// <summary>Permissive category check failed (OR semantics).</summary>
    PermissiveCategoryFailed,

    /// <summary>Enumerated category check failed.</summary>
    EnumeratedCategoryFailed,

    /// <summary>Category validity period has expired or not yet valid.</summary>
    ValidityPeriodExpired,

    /// <summary>Label structure is invalid against SPIF.</summary>
    LabelValidationFailed
}

namespace AbacController.Core.Constants;

/// <summary>
/// XACML 4-valued decision outcomes per NIST SP 800-162.
/// </summary>
public enum Decision
{
    /// <summary>Access is granted.</summary>
    Permit = 1,

    /// <summary>Access is denied.</summary>
    Deny = 2,

    /// <summary>No applicable policy found (default deny applies).</summary>
    NotApplicable = 3,

    /// <summary>Evaluation error; decision cannot be determined.</summary>
    Indeterminate = 4
}

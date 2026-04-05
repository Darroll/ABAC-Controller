namespace AbacController.Api.Runtime;

/// <summary>
/// Tracks coarse application lifecycle state for health probes.
/// </summary>
public sealed class AppRuntimeState
{
    /// <summary>Gets or sets the startup Completed.</summary>
    public bool StartupCompleted { get; private set; }

    /// <summary>
    /// Executes mark Startup Completed.
    /// </summary>
    public void MarkStartupCompleted() => StartupCompleted = true;
}

namespace AbacController.Api.Runtime;

/// <summary>
/// Tracks coarse application lifecycle state for health probes.
/// </summary>
public sealed class AppRuntimeState
{
    /// <summary>Gets a value indicating whether startup has completed.</summary>
    public bool StartupCompleted { get; private set; }

    /// <summary>Marks application startup as complete.</summary>
    public void MarkStartupCompleted() => StartupCompleted = true;
}

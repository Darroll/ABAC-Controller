namespace AbacController.Api.Runtime;

/// <summary>
/// Tracks coarse application lifecycle state for health probes.
/// </summary>
public sealed class AppRuntimeState
{
    public bool StartupCompleted { get; private set; }

    public void MarkStartupCompleted() => StartupCompleted = true;
}

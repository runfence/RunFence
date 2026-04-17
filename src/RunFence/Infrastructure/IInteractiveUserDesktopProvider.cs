namespace RunFence.Infrastructure;

/// <summary>
/// Provides shell folder paths of the interactive (non-elevated) user.
/// </summary>
public interface IInteractiveUserDesktopProvider
{
    string? GetDesktopPath();
    string? GetTaskBarPath();

    /// <summary>
    /// Invalidates cached desktop and taskbar paths. Should be called when the interactive user
    /// changes (e.g., fast user switching via <see cref="Microsoft.Win32.SystemEvents.SessionSwitch"/>)
    /// so subsequent calls resolve fresh paths for the new session.
    /// </summary>
    void InvalidateCache();
}
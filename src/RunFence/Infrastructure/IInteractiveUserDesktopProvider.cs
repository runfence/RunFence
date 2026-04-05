namespace RunFence.Infrastructure;

/// <summary>
/// Provides the desktop path of the interactive (non-elevated) user.
/// </summary>
public interface IInteractiveUserDesktopProvider
{
    string? GetDesktopPath();
}
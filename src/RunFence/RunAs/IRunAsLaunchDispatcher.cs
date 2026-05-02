using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Dispatches the RunAs dialog result to the appropriate action: opening the app edit dialog
/// for new entry creation, or launching the app directly (via existing entry or without one).
/// </summary>
public interface IRunAsLaunchDispatcher
{
    void DispatchContainerResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath);

    void DispatchCredentialResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath);
}

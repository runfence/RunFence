using System.Security;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.RunAs;

/// <summary>
/// Handles launching files without a saved app entry, either via stored credentials or an AppContainer token.
/// Extracted from RunAsAppEntryManager to keep it focused on app entry persistence.
/// </summary>
public class RunAsDirectLauncher(
    IAppStateProvider appState,
    IAppLaunchOrchestrator launchOrchestrator,
    IProcessLaunchService processLaunchService,
    ISidResolver sidResolver,
    IFolderHandlerService folderHandlerService,
    IProfileRepairHelper profileRepair,
    IRunAsLaunchErrorHandler launchErrorHandler)
{
    /// <summary>
    /// Launches a file without a saved app entry, using stored credentials directly.
    /// When <paramref name="adHocPassword"/> is provided, uses it directly without credential lookup.
    /// </summary>
    public void LaunchWithoutAppEntry(string filePath, string? arguments, CredentialEntry credential,
        bool isFolder = false, bool launchAsLowIntegrity = false, bool launchAsSplitToken = false,
        SecureString? adHocPassword = null)
    {
        if (adHocPassword != null)
        {
            var (domain, username) = SidNameResolver.ResolveDomainAndUsername(
                credential.Sid, isCurrentAccount: false, sidResolver, appState.Database.SidNames);
            var launchCreds = new LaunchCredentials(adHocPassword, domain, username);
            var launchFlags = new LaunchFlags(UseSplitToken: launchAsSplitToken, UseLowIntegrity: launchAsLowIntegrity);

            if (isFolder)
            {
                RunWithLaunchErrorHandling(
                    () => profileRepair.ExecuteWithProfileRepair(
                        () =>
                        {
                            processLaunchService.LaunchFolder(filePath,
                                appState.Database.Settings.FolderBrowserExePath,
                                appState.Database.Settings.FolderBrowserArguments,
                                launchCreds, launchFlags);
                            folderHandlerService.Register(credential.Sid);
                        },
                        credential.Sid),
                    filePath);
            }
            else
            {
                var workingDir = Path.GetDirectoryName(filePath) ?? "";
                RunWithLaunchErrorHandling(
                    () => profileRepair.ExecuteWithProfileRepair(
                        () =>
                        {
                            processLaunchService.LaunchExe(new ProcessLaunchTarget(filePath, arguments, workingDir), launchCreds, launchFlags);
                            folderHandlerService.Register(credential.Sid);
                        },
                        credential.Sid),
                    filePath);
            }

            return;
        }

        if (isFolder)
        {
            RunWithLaunchErrorHandling(
                () => launchOrchestrator.LaunchFolderBrowser(credential.Sid, filePath, launchAsLowIntegrity, launchAsSplitToken),
                filePath);
            return;
        }

        var tempApp = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = Path.GetFileNameWithoutExtension(filePath),
            ExePath = filePath,
            AccountSid = credential.Sid,
            LaunchAsLowIntegrity = launchAsLowIntegrity,
            RunAsSplitToken = launchAsSplitToken,
            WorkingDirectory = Path.GetDirectoryName(filePath),
        };
        RunWithLaunchErrorHandling(
            () => launchOrchestrator.Launch(tempApp, arguments),
            filePath);
    }

    /// <summary>
    /// Launches a file without a saved app entry, using an AppContainer token.
    /// </summary>
    public void LaunchWithoutAppEntryInContainer(string filePath, string? arguments,
        AppContainerEntry container, bool isFolder)
    {
        if (isFolder)
        {
            MessageBox.Show("Folder apps are not supported with AppContainers from the Run As flow.",
                "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Build a temporary AppEntry backed by the selected container
        var tempApp = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = Path.GetFileNameWithoutExtension(filePath),
            ExePath = filePath,
            AccountSid = "",
            AppContainerName = container.Name,
            LaunchAsLowIntegrity = true,
        };

        RunWithLaunchErrorHandling(() => launchOrchestrator.Launch(tempApp, arguments), filePath);
    }

    private void RunWithLaunchErrorHandling(Action launchAction, string filePath)
        => launchErrorHandler.RunWithErrorHandling(launchAction, filePath);
}
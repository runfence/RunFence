using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.UI;

/// <summary>
/// Handles the one-time first-run desktop settings export prompt shown from <see cref="Forms.MainForm"/>.
/// By design: runs before startup security checks so that auto-exported ACLs are not reported as issues.
/// </summary>
public class MainFormFirstRunExporter(
    OptionsDesktopSettingsHandler desktopSettingsHandler,
    ILaunchFacade launchFacade,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    ISessionSaver sessionSaver,
    SessionContext session)
    : IMainFormFirstRunExporter
{
    /// <summary>
    /// If no default desktop settings file exists yet, exports it to ProgramData, saves the path,
    /// and offers to open the file for editing. Does nothing if the file already exists.
    /// </summary>
    public async Task PromptExportSettingsIfNeededAsync(IWin32Window owner)
    {
        if (!string.IsNullOrEmpty(session.Database.Settings.DefaultDesktopSettingsPath) &&
            File.Exists(session.Database.Settings.DefaultDesktopSettingsPath))
            return;

        var path = Path.Combine(PathConstants.ProgramDataDir, "settings.json");
        try
        {
            programDataDirectoryProvisioningService.EnsureRoot();
            await desktopSettingsHandler.ExportAsync(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Failed to export desktop settings: {ex.Message}", "Export Desktop Settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        session.Database.Settings.DefaultDesktopSettingsPath = path;
        sessionSaver.SaveConfig();

        if (!DebugHelper.IsDebugBuild)
#pragma warning disable CS0162 // Unreachable code detected
        {
            var open = MessageBox.Show(owner,
                $"Desktop settings exported to {path}.\n\nOpen for editing?",
                "Export Desktop Settings",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (open == DialogResult.Yes)
            {
                using var launch = launchFacade.LaunchFile(
                    path,
                    AccountLaunchIdentity.InteractiveUser with
                    {
                        AssociationResolutionPolicy = AssociationResolutionPolicy.AllowAccountRedirection
                    });
                launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Desktop settings", LaunchFeedbackSource.InteractiveUi)
                {
                    Owner = owner,
                    WarningCaption = "Export Desktop Settings"
                });
            }
        }
#pragma warning restore CS0162 // Unreachable code detected
    }
}

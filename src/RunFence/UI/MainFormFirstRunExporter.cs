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
    ISessionSaver sessionSaver,
    SessionContext session)
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

        var path = Path.Combine(Constants.ProgramDataDir, "settings.json");
        try
        {
            Directory.CreateDirectory(Constants.ProgramDataDir);
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

        var open = MessageBox.Show(owner,
            $"Desktop settings exported to {path}.\n\nOpen for editing?",
            "Export Desktop Settings",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (open == DialogResult.Yes)
            launchFacade.LaunchFile(path, AccountLaunchIdentity.InteractiveUser);
    }
}

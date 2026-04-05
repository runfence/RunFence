using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.UI;

/// <summary>
/// Handles folder browser path validation and browsing for <see cref="RunFence.UI.Forms.OptionsPanel"/>.
/// Validates, commits, and browses the custom folder browser executable setting.
/// </summary>
public class OptionsFolderBrowserHandler
{
    /// <summary>
    /// Validates the given exe path and commits it to settings if valid.
    /// Returns an error message if the path is rejected, or null on success.
    /// </summary>
    public string? ValidateAndCommitExePath(string path, AppSettings settings)
    {
        if (Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            return "explorer.exe does not support running under other accounts and cannot be used as the folder browser.";

        settings.FolderBrowserExePath = path;
        return null;
    }

    /// <summary>
    /// Commits the folder browser arguments to settings.
    /// </summary>
    public void SetArguments(string args, AppSettings settings)
    {
        settings.FolderBrowserArguments = args;
    }

    /// <summary>
    /// Shows an open file dialog for selecting the folder browser executable.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseExe()
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Programs (*.exe;*.cmd;*.bat;*.ps1;*.com)|*.exe;*.cmd;*.bat;*.ps1;*.com|All files (*.*)|*.*";
        dlg.Title = "Select Folder Browser Application";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }
}
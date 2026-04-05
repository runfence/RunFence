using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles file/folder browse dialogs and path validation for <see cref="AppEditDialog"/>.
/// Returns results without touching dialog controls directly.
/// </summary>
public class AppEditBrowseHelper
{
    /// <summary>
    /// Shows an open file dialog for selecting an application executable.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseFile()
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = Constants.AppFileDialogFilter;
        dlg.Title = "Select File";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    /// <summary>
    /// Shows a folder browser dialog for selecting a folder app path.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select Folder";
        dlg.UseDescriptionForTitle = true;

        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>
    /// Shows a folder browser dialog for selecting a working directory.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseWorkingDir(string currentPath)
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select Working Directory";
        dlg.UseDescriptionForTitle = true;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            dlg.InitialDirectory = currentPath;

        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }
}
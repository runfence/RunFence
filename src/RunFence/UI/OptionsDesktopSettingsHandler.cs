using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.PrefTrans;

namespace RunFence.UI;

/// <summary>
/// Handles desktop settings path configuration and export for <see cref="RunFence.UI.Forms.OptionsPanel"/>.
/// </summary>
public class OptionsDesktopSettingsHandler(
    ISettingsTransferService settingsTransferService,
    IOpenFileDialogAdapterFactory openFileDialogFactory)
{
    /// <summary>
    /// Commits the default desktop settings path to settings.
    /// </summary>
    public void SetDesktopSettingsPath(string path, AppSettings settings)
    {
        settings.DefaultDesktopSettingsPath = path;
    }

    /// <summary>
    /// Shows an open file dialog for selecting a desktop settings file.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseDesktopSettings()
    {
        using var dlgAdapter = openFileDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.Title = "Select Default Desktop Settings File";

        return dlgAdapter.ShowDialog(owner: null) == DialogResult.OK ? dlg.FileName : null;
    }

    /// <summary>
    /// Exports desktop settings to the given output path asynchronously.
    /// Throws <see cref="InvalidOperationException"/> on export failure.
    /// </summary>
    public async Task ExportAsync(string outputPath)
    {
        var result = await Task.Run(() => settingsTransferService.ExportDesktopSettings(outputPath));

        if (!result.Success)
            throw new InvalidOperationException(result.Message);
    }

}

using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.PrefTrans.UI.Forms;

/// <summary>
/// UI helpers for the desktop settings import file dialog.
/// </summary>
public static class DesktopSettingsImportDialog
{
    /// <summary>
    /// Configures a FileDialog for selecting a desktop settings JSON file.
    /// </summary>
    public static void Setup(FileDialog dlg, string? lastPrefsFilePath)
    {
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.DefaultExt = "json";
        dlg.FileName = "settings.json";
        dlg.InitialDirectory = Constants.ProgramDataDir;

        if (!string.IsNullOrEmpty(lastPrefsFilePath))
        {
            try
            {
                dlg.InitialDirectory = Path.GetDirectoryName(lastPrefsFilePath) ?? Constants.ProgramDataDir;
                dlg.FileName = Path.GetFileName(lastPrefsFilePath);
            }
            catch
            {
                /* best effort */
            }
        }

        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
    }
}
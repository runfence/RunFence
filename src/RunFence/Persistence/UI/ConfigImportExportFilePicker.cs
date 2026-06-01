using RunFence.Core.Infrastructure;

namespace RunFence.Persistence.UI;

public class ConfigImportExportFilePicker : IConfigImportExportFilePicker
{
    public string? SelectSavePath(string filter, string title)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = filter,
            Title = title
        };
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    public string? SelectOpenPath(string filter, string title)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title
        };
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }
}

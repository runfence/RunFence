using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall.UI;

/// <summary>
/// Handles import and export UI flows for the firewall allowlist dialog:
/// file-open/save dialogs, result reporting, and import/export parsing.
/// Separates the import/export IO interactions from the dialog's main concern
/// of editing allowlist and port entries.
/// </summary>
public class FirewallAllowlistImportExportHelper(
    FirewallAllowlistImportExportService importExportService,
    Func<IReadOnlyList<FirewallAllowlistEntry>> getAllowlistEntries,
    Func<IReadOnlyList<string>> getPortEntries,
    IFirewallAllowlistDialogView view) : IFirewallAllowlistImportExportFlow
{
    /// <summary>
    /// Runs the import flow: prompts user to select a file, parses it, and returns valid entries
    /// for the controller to add into the dialog grids. Shows appropriate messages on errors or no new entries.
    /// </summary>
    public FirewallAllowlistImportedLines? Import()
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.Title = "Import Firewall Settings";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(view) != DialogResult.OK)
            return null;

        var fileResult = importExportService.ImportFromFile(dlg.FileName);
        if (fileResult.Lines == null)
        {
            view.ShowError("Error", $"Import failed: {fileResult.ErrorMessage ?? "Unknown error"}");
            return null;
        }

        return new FirewallAllowlistImportedLines(
            fileResult.Lines.AllowlistLines,
            fileResult.Lines.PortLines);
    }

    /// <summary>
    /// Prompts the user to select a save location, then exports the provided entries.
    /// Returns true on success, false if the user cancelled or export failed.
    /// </summary>
    public bool TryExportToFile(IReadOnlyList<string> entries, string title)
    {
        if (entries.Count == 0)
        {
            view.ShowInformation("Export", "Nothing to export.");
            return false;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = title;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(view) != DialogResult.OK)
            return false;

        var exportEntries = entries.Select(v => new FirewallAllowlistEntry { Value = v }).ToList();
        var error = importExportService.ExportToFile(dlg.FileName, exportEntries);
        if (error != null)
        {
            view.ShowError("Error", $"Export failed: {error}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Prompts the user to select a save location, then exports all allowlist entries and
    /// port exceptions combined into a single file.
    /// </summary>
    public void TryExportCombinedToFile()
    {
        var allEntries = getAllowlistEntries();
        var allPorts = getPortEntries();
        if (allEntries.Count == 0 && allPorts.Count == 0)
        {
            view.ShowInformation("Export", "Nothing to export.");
            return;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = "Export Firewall Settings";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(view) != DialogResult.OK)
            return;

        var error = importExportService.ExportCombinedToFile(dlg.FileName, allEntries, allPorts);
        if (error != null)
            view.ShowError("Error", $"Export failed: {error}");
    }
}

public sealed record FirewallAllowlistImportedLines(
    IReadOnlyList<string> AllowlistLines,
    IReadOnlyList<string> PortLines);

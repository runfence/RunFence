using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall.UI;

/// <summary>
/// Handles import and export UI flows for the firewall allowlist dialog:
/// file-open/save dialogs, result reporting, and grid updates.
/// Separates the import/export IO interactions from the dialog's main concern
/// of editing allowlist and port entries.
/// </summary>
public class FirewallAllowlistImportExportHelper(
    FirewallAllowlistImportExportService importExportService,
    FirewallAllowlistTabHandler allowlistHandler,
    FirewallPortsTabHandler portsHandler,
    FirewallAllowlistGridHelper allowlistGridHelper,
    FirewallPortsGridHelper portsGridHelper,
    IWin32Window owner)
{
    /// <summary>
    /// Runs the import flow: prompts user to select a file, parses it, and adds valid entries
    /// to the grid helpers. Shows appropriate messages on errors or no new entries.
    /// </summary>
    public void OnImportClick()
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.Title = "Import Firewall Settings";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return;

        var fileResult = importExportService.ImportFromFile(dlg.FileName);
        if (fileResult.Lines == null)
        {
            MessageBox.Show($"Import failed: {fileResult.ErrorMessage ?? "Unknown error"}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var allowlistResult = allowlistHandler.ImportLines(fileResult.Lines.AllowlistLines);
        var portsResult = portsHandler.ImportLines(fileResult.Lines.PortLines);

        if (allowlistResult.AddedEntries.Count == 0 && portsResult.AddedPorts.Count == 0)
        {
            MessageBox.Show("No new entries to import (all duplicates or invalid).", "Import",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        allowlistGridHelper.AddImportedEntries(allowlistResult.AddedEntries);
        portsGridHelper.AddImportedPorts(portsResult.AddedPorts);

        var parts = new List<string>();
        if (allowlistResult.AddedEntries.Count > 0)
            parts.Add($"{allowlistResult.AddedEntries.Count} {(allowlistResult.AddedEntries.Count == 1 ? "allowlist entry" : "allowlist entries")}");
        if (portsResult.AddedPorts.Count > 0)
            parts.Add($"{portsResult.AddedPorts.Count} {(portsResult.AddedPorts.Count == 1 ? "port exception" : "port exceptions")}");
        var msg = $"Imported {string.Join(" and ", parts)}.";
        if (allowlistResult.EntryLimitReached)
            msg += $"\n\n{allowlistResult.LicenseLimitMessage}";
        if (portsResult.PortLimitReached)
            msg += $"\n\nMaximum of {LocalhostPortParser.MaxAllowedPorts} port entries reached.";
        MessageBox.Show(msg, "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Prompts the user to select a save location, then exports the provided entries.
    /// Returns true on success, false if the user cancelled or export failed.
    /// </summary>
    public bool TryExportToFile(IReadOnlyList<string> entries, string title)
    {
        if (entries.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = title;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return false;

        var exportEntries = entries.Select(v => new FirewallAllowlistEntry { Value = v }).ToList();
        var error = importExportService.ExportToFile(dlg.FileName, exportEntries);
        if (error != null)
        {
            MessageBox.Show($"Export failed: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        var allEntries = allowlistHandler.GetEntries();
        var allPorts = portsHandler.GetPortEntries();
        if (allEntries.Count == 0 && allPorts.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = "Export Firewall Settings";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return;

        var error = importExportService.ExportCombinedToFile(dlg.FileName, allEntries, allPorts);
        if (error != null)
            MessageBox.Show($"Export failed: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

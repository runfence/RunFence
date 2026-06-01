namespace RunFence.Firewall.UI;

public sealed class FirewallAllowlistImportExportCoordinator(
    IFirewallAllowlistImportExportFlow importExportFlow,
    FirewallAllowlistEntriesController allowlistEntriesController,
    FirewallAllowlistPortsController portsController,
    IFirewallAllowlistDialogView view)
{
    public void HandleImport()
    {
        var result = importExportFlow.Import();
        if (result != null)
        {
            var allowlistResult = allowlistEntriesController.ImportLines(result.AllowlistLines);
            var portsResult = portsController.ImportLines(result.PortLines);

            if (allowlistResult.AddedEntries.Count == 0 && portsResult.AddedPorts.Count == 0)
            {
                view.ShowInformation("Import", "No new entries to import (all duplicates or invalid).");
            }
            else
            {
                allowlistEntriesController.AddImportedEntries(allowlistResult.AddedEntries);
                portsController.AddImportedPorts(portsResult.AddedPorts);

                var parts = new List<string>();
                if (allowlistResult.AddedEntries.Count > 0)
                    parts.Add($"{allowlistResult.AddedEntries.Count} {(allowlistResult.AddedEntries.Count == 1 ? "allowlist entry" : "allowlist entries")}");
                if (portsResult.AddedPorts.Count > 0)
                    parts.Add($"{portsResult.AddedPorts.Count} {(portsResult.AddedPorts.Count == 1 ? "port exception" : "port exceptions")}");

                var message = $"Imported {string.Join(" and ", parts)}.";
                if (allowlistResult.EntryLimitReached)
                    message += $"\n\n{allowlistResult.LicenseLimitMessage}";
                if (portsResult.PortLimitReached)
                    message += $"\n\nMaximum of {LocalhostPortParser.MaxAllowedPorts} port entries reached.";

                view.ShowInformation("Import", message);
            }
        }

        view.UpdateApplyButton();
    }

    public void HandleExport(bool internetTabSelected)
    {
        if (internetTabSelected)
            allowlistEntriesController.ExportSelected();
        else
            portsController.ExportSelected();
    }
}

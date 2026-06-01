using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

public sealed class FirewallAllowlistEntriesController(
    IFirewallAllowlistDialogView view,
    FirewallAllowlistTabHandler allowlistHandler,
    FirewallAllowlistGridHelper allowlistGridHelper)
{
    public bool IsResolvingDomains => allowlistGridHelper.IsResolvingDomains;

    public void Initialize()
    {
        allowlistGridHelper.PopulateGrid();

        if (allowlistHandler.HasDomainEntries())
            _ = allowlistGridHelper.ResolveDomainEntriesAsync(false, CancellationToken.None);
    }

    public void HandleAdd()
    {
        var input = view.PromptInput("Add Entry", "Enter an IP address, CIDR range, or domain name:");
        if (input != null)
            allowlistGridHelper.AddEntry(input);
    }

    public void HandleRemove() => allowlistGridHelper.RemoveSelected();

    public void ExportSelected() => allowlistGridHelper.ExportSelected();

    public async Task HandleResolveDomainsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!allowlistHandler.HasDomainEntries())
        {
            view.ShowInformation("Resolve", "No domain entries to resolve.");
            return;
        }

        await allowlistGridHelper.ResolveDomainEntriesAsync(true, cancellationToken);
    }

    public void PopulateGrid() => allowlistGridHelper.PopulateGrid();

    public void HandleCellEndEdit(int rowIndex, int columnIndex) =>
        allowlistGridHelper.ApplyCellEdit(rowIndex, columnIndex);

    public void HandleMouseDown(int x, int y) => allowlistGridHelper.HandleMouseDown(x, y);

    public void ConfigureContextMenu() => allowlistGridHelper.ConfigureContextMenu();

    public bool HandleKeyDown(Keys keyCode, bool control) => allowlistGridHelper.HandleKeyDown(keyCode, control);

    public void AddImportedEntries(IReadOnlyList<FirewallAllowlistEntry> entries) =>
        allowlistGridHelper.AddImportedEntries(entries);

    public AllowlistImportResult ImportLines(IReadOnlyList<string> lines) =>
        allowlistHandler.ImportLines(lines);

    public IReadOnlyList<FirewallAllowlistEntry> GetEntries() => allowlistHandler.GetEntries();

    public string? GetLicenseLimitMessage() => allowlistHandler.GetLicenseLimitMessage();

    public BlockedConnectionAddResult AddEntriesFromBlockedConnections(
        IEnumerable<FirewallAllowlistEntry> selectedEntries) =>
        allowlistGridHelper.AddEntriesFromBlockedConnections(selectedEntries);
}

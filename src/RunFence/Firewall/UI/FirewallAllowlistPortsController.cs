namespace RunFence.Firewall.UI;

public sealed class FirewallAllowlistPortsController(
    IFirewallAllowlistDialogView view,
    FirewallPortsTabHandler handler,
    FirewallPortsGridHelper portsGridHelper)
{
    public void Initialize() => portsGridHelper.PopulatePortsGrid();

    public void HandleAdd()
    {
        var input = view.PromptInput("Add Port Exception", "Enter a port number or range (e.g. 53, 8080-8090):");
        if (input != null)
            portsGridHelper.AddPort(input);
    }

    public void HandleRemove() => portsGridHelper.RemoveSelected();

    public void ExportSelected() => portsGridHelper.ExportSelected();

    public void HandleCellEndEdit(int rowIndex, int columnIndex) =>
        portsGridHelper.ApplyCellEdit(rowIndex, columnIndex);

    public void HandleMouseDown(int x, int y) => portsGridHelper.HandleMouseDown(x, y);

    public void ConfigureContextMenu() => portsGridHelper.ConfigureContextMenu();

    public bool HandleKeyDown(Keys keyCode) => portsGridHelper.HandleKeyDown(keyCode);

    public void AddImportedPorts(IReadOnlyList<string> ports) => portsGridHelper.AddImportedPorts(ports);

    public PortImportResult ImportLines(IReadOnlyList<string> lines) =>
        handler.ImportLines(lines);
}

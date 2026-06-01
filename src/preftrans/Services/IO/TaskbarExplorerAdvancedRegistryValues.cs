namespace PrefTrans.Services.IO;

public sealed record TaskbarExplorerAdvancedRegistryValues
{
    public int? TaskbarSmallIcons { get; init; }
    public int? ShowTaskViewButton { get; init; }
    public int? TaskbarAlignment { get; init; }
    public int? ShowWidgets { get; init; }
    public int? ButtonCombine { get; init; }
    public int? MultiMonitorButtonCombine { get; init; }
    public int? VirtualDesktopTaskbarFilter { get; init; }
}

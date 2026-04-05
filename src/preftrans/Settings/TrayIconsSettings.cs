namespace PrefTrans.Settings;

public class TrayIconEntry
{
    public string? ExecutablePath { get; set; }
    public int? IsPromoted { get; set; }
}

public class TrayIconsSettings
{
    public int? EnableAutoTray { get; set; }
    public List<TrayIconEntry>? PerAppVisibility { get; set; }
}
namespace PrefTrans.Settings;

public class TaskbarSettings
{
    public int? SmallIcons { get; set; }
    public int? ShowTaskViewButton { get; set; }
    public int? TaskbarAlignment { get; set; }
    public int? ShowWidgets { get; set; }
    public int? ButtonCombine { get; set; }
    public int? MultiMonitorButtonCombine { get; set; }
    public int? VirtualDesktopTaskbarFilter { get; set; }
    public int? SearchboxTaskbarMode { get; set; } // 0=hidden, 1=icon, 2=search box
    public byte[]? Favorites { get; set; }

    public byte[]? FavoritesResolve { get; set; }

    // Profile path of the account that exported this file.
    // Used to patch binary blobs when importing into a different account.
    public string? SourceProfilePath { get; set; }

    // Filename → binary content of each pinned TaskBar shortcut.
    // Stored at export time so they can be written to the target account's TaskBar folder on import,
    // since the target user may not have read access to the source account's AppData.
    public Dictionary<string, byte[]>? PinnedShortcutFiles { get; set; }

    // Legacy: shortcut filenames only (no binary content). Kept for backward compatibility.
    public List<string>? PinnedShortcuts { get; set; }
}
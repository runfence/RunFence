namespace PrefTrans.Settings;

public class ExplorerSettings
{
    public int? ShowHiddenFiles { get; set; }
    public int? ShowFileExtensions { get; set; }
    public int? ShowSuperHidden { get; set; }
    public int? ShowFullPathInTitleBar { get; set; }
    public int? LaunchFolderInSeparateProcess { get; set; }
    public int? ShowStatusBar { get; set; }
    public int? UseCompactMode { get; set; }
    public int? AutoCheckSelect { get; set; }
    public int? NavPaneExpandToCurrentFolder { get; set; }
    public int? ShowSecondsInClock { get; set; }
    public int? StartTrackDocs { get; set; }
    public int? StartShowFrequent { get; set; }
    public int? StartShowRecent { get; set; }
    public int? SnapAssist { get; set; }
    public int? EnableSnapBar { get; set; }
    public int? EnableSnapAssistFlyout { get; set; }
    public int? EnableClipboardHistory { get; set; }
    public int? TaskbarEndTask { get; set; } // Explorer\Advanced\TaskbarEndTask
    public int? SerializeStartupDelay { get; set; } // Explorer\Serialize\StartupDelayInMSec
}
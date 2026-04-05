// ReSharper disable InconsistentNaming

namespace PrefTrans.Native;

public static class Constants
{
    // SystemParametersInfo actions
    public const uint SPI_GETMOUSE = 0x0003;
    public const uint SPI_SETMOUSE = 0x0004;
    public const uint SPI_GETKEYBOARDSPEED = 0x000A;
    public const uint SPI_SETKEYBOARDSPEED = 0x000B;
    public const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    public const uint SPI_GETSCREENSAVETIMEOUT = 0x000E;
    public const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    public const uint SPI_GETSCREENSAVEACTIVE = 0x0010;
    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPI_GETKEYBOARDDELAY = 0x0016;
    public const uint SPI_SETKEYBOARDDELAY = 0x0017;
    public const uint SPI_SETDOUBLECLICKTIME = 0x0020;
    public const uint SPI_GETMOUSESPEED = 0x0070;
    public const uint SPI_SETMOUSESPEED = 0x0071;

    // SystemParametersInfo flags
    public const uint SPIF_UPDATEINIFILE = 0x0001;
    public const uint SPIF_SENDCHANGE = 0x0002;
    public const uint SPIF_UPDATEANDNOTIFY = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;

    // GetSystemMetrics
    public const int SM_SWAPBUTTON = 23;

    // SendMessageTimeout
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // SHChangeNotify
    public const uint SHCNE_ALLEVENTS = 0x7FFFFFFF;
    public const uint SHCNF_IDLIST = 0x0000;

    // Registry paths
    public const string RegCursors = @"Control Panel\Cursors";
    public const string RegDesktop = @"Control Panel\Desktop";
    public const string RegExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string RegWallpapers = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
    public const string RegThemesPersonalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string RegDWM = @"Software\Microsoft\Windows\DWM";
    public const string RegKeyboardLayout = "Keyboard Layout";
    public const string RegAccessibility = @"Control Panel\Accessibility";
    public const string RegInternational = @"Control Panel\International";
    public const string RegUserShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    public const string RegEnvironment = "Environment";
    public const string RegNotifyIconSettings = @"Control Panel\NotifyIconSettings";
    public const string RegExplorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    public const string RegNotificationSettings = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    public const string RegClipboard = @"Software\Microsoft\Clipboard";
    public const string RegFileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
    public const string RegUserClasses = @"Software\Classes";

    public const string RegNightLightState =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    public const string RegNightLightSettings =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

    public const string RegTaskband = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    public const string RegSearch = @"Software\Microsoft\Windows\CurrentVersion\Search";
    public const string RegExplorerSerialize = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
    public const string RegKeyboard = @"Control Panel\Keyboard";

    // SHChangeNotify for file association changes
    public const uint SHCNE_ASSOCCHANGED = 0x08000000;

    public static readonly HashSet<string> BlockedEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEMP", "TMP", "APPDATA", "LOCALAPPDATA",
        "HOMEDRIVE", "HOMEPATH", "HOMESHARE",
        "OneDrive", "OneDriveConsumer", "OneDriveCommercial",
        "PATH", "PATHEXT",
    };

    public static readonly List<string> BlockedEnvVarsParts = ["AUTH", "KEY", "PASS", "USERNAME"];

    public static readonly string[] TrackedFileExtensions =
    [
        ".pdf", ".htm", ".html", ".ini", ".txt", ".url", ".cfg", ".log", ".md", ".torrent",
        ".mp3", ".mp4", ".mov", ".avi", ".wmv", ".wav",
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
        ".zip", ".rar", ".7z", ".gz", ".tar",
        ".ps1", ".odt", ".docx", ".xlsx", ".xls", ".csv",
        ".json", ".xml", ".yaml", ".yml"
    ];
}

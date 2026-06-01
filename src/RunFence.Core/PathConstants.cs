namespace RunFence.Core;

public static class PathConstants
{
    public const string AppName = "RunFence";

    // When AppId is set (debug builds), isolate each exe-directory instance under
    // %LocalAppData%\RunFenceDebug\<AppId>\ so that multiple side-by-side debug instances
    // never share data, configuration, or registry entries.
    private static string DebugDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RunFenceDebug",
            DebugHelper.AppId ?? "Default");

    public static string RoamingAppDataDir =>
        !string.IsNullOrEmpty(DebugHelper.AppId)
            ? Path.Combine(DebugDataRoot, "Roaming")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LocalAppDataDir =>
        !string.IsNullOrEmpty(DebugHelper.AppId)
            ? Path.Combine(DebugDataRoot, "Local")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string ProgramDataDir =>
        !string.IsNullOrEmpty(DebugHelper.AppId) || DebugHelper.UseAdminOperationMocks
            ? Path.Combine(DebugDataRoot, "ProgramData")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName);

    // License
    public static string LicenseFilePath => Path.Combine(RoamingAppDataDir, "license.dat");
    public static readonly string LicenseRegistryKey = @"Software\RunFence" + DebugHelper.AppIdSuffix;
    public const string LastNagShownValueName = "LastNagShownDate";

    // ACL
    public const int MaxFolderAclDepth = 5;

    private static readonly Lazy<string[]> BlockedAclPaths = new(ComputeBlockedAclPaths);

    public static string[] GetBlockedAclPaths() => BlockedAclPaths.Value;

    private static string[] ComputeBlockedAclPaths()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDrive = Path.GetPathRoot(windowsDir)?.TrimEnd(Path.DirectorySeparatorChar);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(systemDrive))
            paths.Add(systemDrive);
        if (!string.IsNullOrEmpty(windowsDir))
            paths.Add(windowsDir);
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(sysDir))
            paths.Add(sysDir);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            paths.Add(pf);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pfx86))
            paths.Add(pfx86);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var usersDir = Path.GetDirectoryName(userProfile);
        if (!string.IsNullOrEmpty(usersDir))
            paths.Add(usersDir);
        return paths.ToArray();
    }

    // Executable names
    public const string LauncherExeName = "RunFence.Launcher.exe";
    public const string FolderBrowserExeName = "RunFence.FolderBrowser.exe";
    public const string DragBridgeExeName = "RunFence.DragBridge.exe";
    public const string JobKeeperExeName = "RunFence.JobKeeper.exe";
    public const string PinHelperExeName = "RunFence.PinHelper.exe";
    public const string ProfileKeeperExeName = "RunFence.ProfileKeeper.exe";
    public const string AppxLauncherExeName = "RunFence.AppxLauncher.exe";
    public const string ShellServerExeName = "RunFence.ShellServer.exe";
    public const string PrefTransExeName = "preftrans.exe";
    public const string SecurityScannerExeName = "RunFence.SecurityScanner.exe";

    // Folder handler
    public const string FolderHandlerUnregisterScriptName = "unregister-folder-handler.cmd";
    public static readonly string FolderHandlerRunOnceValueName = "RunFence" + DebugHelper.AppIdSuffix + "_FolderHandler";

    // DragBridge
    public const string DragBridgeTempDir = "DragBridge";

    // Logging
    public const long MaxLogFileSize = 5 * 1024 * 1024; // 5 MB
    public static string LogFilePath => Path.Combine(LocalAppDataDir, "runfence.log");

    // Registry
    public const string ProfileListRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    // Handler registration
    /// <summary>Prefix for per-association ProgIds (e.g., "RunFence_http", "RunFence_.pdf").</summary>
    public static readonly string HandlerProgIdPrefix = "RunFence" + DebugHelper.AppIdSuffix + "_";

    /// <summary>
    /// HKLM-relative registry path for the Capabilities subkey shown in Windows Settings > Default Apps.
    /// Kept outside Software\Classes (standard location) to prevent Windows from double-discovering
    /// RunFence via both RegisteredApplications and the ProgId namespace.
    /// </summary>
    public static readonly string HandlerCapabilitiesRegistryPath =
        @"Software\RunFence" + DebugHelper.AppIdSuffix + @"\Capabilities";

    /// <summary>Name shown in Windows Settings > Default Apps and RegisteredApplications.</summary>
    public static readonly string HandlerRegisteredAppName =
        !string.IsNullOrEmpty(DebugHelper.AppId) ? $"RunFence ({DebugHelper.AppId})" : "RunFence";

    /// <summary>Registry value name used to store the original handler before RunFence overrides it.</summary>
    public const string RunFenceFallbackValueName = "RunFenceFallback";

    /// <summary>
    /// Associations that require the Windows Default Apps UI (Windows ignores HKCU overrides for these).
    /// HKCU auto-set skips these; HKLM ProgIds and Capabilities still include them.
    /// </summary>
    public static readonly HashSet<string> DefaultAppsOnlyAssociations =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", ".htm", ".html", ".pdf", "ftp" };

    // File dialog filter for app file browsing
    public const string AppFileDialogFilter =
        "Programs and Scripts (*.exe;*.msi;*.cmd;*.bat;*.ps1;*.com;*.scr)|*.exe;*.msi;*.cmd;*.bat;*.ps1;*.com;*.scr" +
        "|Shortcuts and Other (*.lnk;*.reg)|*.lnk;*.reg|All files (*.*)|*.*";

    // Extensions that app discovery considers valid app targets (excludes .lnk, .reg)
    public static readonly HashSet<string> DiscoverableExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".cmd", ".bat", ".ps1", ".com", ".scr", ".msi" };

    // URL Schemes
    public static readonly string[] BlockedUrlSchemes =
    [
        "file",
        "ms-msdt",
        "search-ms"
    ];

    // Unlock cmd script — static file deployed alongside RunFence.exe in the app directory.
    // Access is controlled via PathGrantService.EnsureAccess on the app directory at startup.
    public static string UnlockCmdPath =>
        Path.Combine(AppContext.BaseDirectory, "unlock.cmd");

    public static string UnlockOperationCmdPath =>
        Path.Combine(AppContext.BaseDirectory, "unlock-operation.cmd");

    // Context menu
    public static string ExportedIconPath =>
        Path.Combine(ProgramDataDir, "RunFence.ico");

    public static readonly string[] ContextMenuRegistryPaths = BuildContextMenuRegistryPaths();

    private static string[] BuildContextMenuRegistryPaths()
    {
        var suffix = !string.IsNullOrEmpty(DebugHelper.AppId) ? $" ({DebugHelper.AppId})" : "";
        return
        [
            $@"SOFTWARE\Classes\exefile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\cmdfile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\batfile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\comfile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\scrfile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\Msi.Package\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\Microsoft.PowerShellScript.1\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\regfile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\lnkfile\shell\RunFence{suffix}",
            $@"SOFTWARE\Classes\Directory\shell\RunFence{suffix}",
        ];
    }
}

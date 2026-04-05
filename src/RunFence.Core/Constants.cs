namespace RunFence.Core;

public static class Constants
{
    public const string AppName = "RunFence";

    // Versioning
    public const int MajorVersion = 1;

    // Evaluation limits
    public const int EvaluationMaxApps = 3;
    public const int EvaluationMaxContainers = 1;
    public const int EvaluationMaxHiddenAccounts = 1;
    public const int EvaluationMaxCredentials = 3;
    public const int EvaluationMaxFirewallAllowlistEntries = 1;

    // License file
    public static string LicenseFilePath => Path.Combine(RoamingAppDataDir, "license.dat");

    // Registry
    public const string LicenseRegistryKey = @"Software\RunFence";
    public const string LastNagShownValueName = "LastNagShownDate";

    // Mutex
    public const string MutexName = @"Global\RunFence";

    // Pipe
    public const string PipeName = "RunFence";
    public const int MaxPipeMessageSize = 64 * 1024; // 64 KB

    // Timeouts
    public const int LauncherTimeoutMs = 30_000; // 30 seconds

    public const int PipeConnectTimeoutMs = 5_000;

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

    // File paths
    public static string RoamingAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LocalAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string ProgramDataIconDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName, "icons");

    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppName);

    // Crypto
    public const int Argon2MemoryMb = 1024;
    public const int Argon2Iterations = 3;
    public const int Argon2Parallelism = 2;
    public const int Argon2OutputBytes = 32;
    public const int Argon2SaltSize = 32;
    public static byte[] PinCanaryPlaintext => "RunAsMgr-PIN-OK!"u8.ToArray();

    // Launcher
    public const string LauncherExeName = "RunFence.Launcher.exe";

    // Folder browser
    public const string FolderBrowserExeName = "RunFence.FolderBrowser.exe";

    // Drag bridge
    public const string DragBridgeExeName = "RunFence.DragBridge.exe";

    // Shell Windows COM server (intercepts SHOpenFolderAndSelectItems in RunAs browser accounts)
    public const string ShellServerExeName = "RunFence.ShellServer.exe";

    // Folder handler unregistration (RunOnce script run when browser account logs in)
    public const string FolderHandlerUnregisterScriptName = "unregister-folder-handler.cmd";
    public const string FolderHandlerRunOnceValueName = "RunFence_FolderHandler";

    public const string DragBridgeTempDir = "DragBridge";
    public const int DragBridgePipeConnectTimeoutMs = 10_000;

    // Settings transfer
    public const string PrefTransExeName = "preftrans.exe";

    // Security scanner
    public const string SecurityScannerExeName = "RunFence.SecurityScanner.exe";

    // Registry paths
    public const string ProfileListRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

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
    // Access is controlled via PermissionGrantService.EnsureAccess on the app directory at startup.
    public static string UnlockCmdPath =>
        Path.Combine(AppContext.BaseDirectory, "unlock.cmd");

    // Logging
    public const long MaxLogFileSize = 5 * 1024 * 1024; // 5 MB
    public static string LogFilePath => Path.Combine(LocalAppDataDir, "runfence.log");

    // Handler registration
    /// <summary>Prefix for per-association ProgIds (e.g., "RunFence_http", "RunFence_.pdf").</summary>
    public const string HandlerProgIdPrefix = "RunFence_";

    /// <summary>Shared parent ProgId whose Capabilities subkey registers one "RunFence" app in Windows Settings.</summary>
    public const string HandlerParentKey = "RunFence_Handler";

    /// <summary>Name shown in Windows Settings > Default Apps and RegisteredApplications.</summary>
    public const string HandlerRegisteredAppName = "RunFence";

    // Context menu
    public static string ExportedIconPath =>
        Path.Combine(ProgramDataDir, "RunFence.ico");

    public static readonly string[] ContextMenuRegistryPaths =
    [
        @"SOFTWARE\Classes\exefile\shell\RunFence",
        @"SOFTWARE\Classes\cmdfile\shell\RunFence",
        @"SOFTWARE\Classes\batfile\shell\RunFence",
        @"SOFTWARE\Classes\comfile\shell\RunFence",
        @"SOFTWARE\Classes\scrfile\shell\RunFence",
        @"SOFTWARE\Classes\Msi.Package\shell\RunFence",
        @"SOFTWARE\Classes\Microsoft.PowerShellScript.1\shell\RunFence",
        @"SOFTWARE\Classes\regfile\shell\RunFence",
        @"SOFTWARE\Classes\lnkfile\shell\RunFence",
        @"SOFTWARE\Classes\Directory\shell\RunFence",
    ];
}
using Microsoft.Win32;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.UI;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for specifying game launcher executables.
/// Auto-detects known launcher paths on activation via registry and standard SpecialFolder paths;
/// user can add/remove entries.
/// </summary>
public class GamingLaunchersStep : WizardStepPage
{
    private readonly Action<List<string>> _setLauncherPaths;
    private readonly IShortcutDiscoveryService _discoveryService;
    private readonly Func<string?>? _getAccountSid;

    private FolderListEditor _editor = null!;
    private ToolStripButton _discoverButton = null!;
    private ToolStripMenuItem _ctxDiscover = null!;

    private static readonly Func<string?>[] KnownLauncherDetectors =
    [
        // Steam: check registry (supports custom install drive), fall back to default path
        () => TryRegistry(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "steam.exe")
              ?? TryRegistry(@"SOFTWARE\Valve\Steam", "InstallPath", "steam.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFilesX86, @"Steam\steam.exe"),
        // Epic Games Launcher: installer location changed between versions (x86 vs non-x86)
        () => TryPath(Environment.SpecialFolder.ProgramFilesX86, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFiles, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
        // GOG Galaxy: registry 'client' value is the full exe path; also check both Program Files locations
        () => TryRegistry(@"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient", "client", null)
              ?? TryPath(Environment.SpecialFolder.ProgramFilesX86, @"GOG Galaxy\GalaxyClient.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFiles, @"GOG Galaxy\GalaxyClient.exe"),
        // EA Desktop (newer branding) / EA App (older branding)
        () => TryPath(Environment.SpecialFolder.ProgramFiles, @"Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFiles, @"EA\EA App\EALauncher.exe"),
        // Ubisoft Connect: registry InstallDir points to launcher dir; exe may be named UbisoftGameLauncher.exe or Ubisoft Game Launcher.exe
        () => TryRegistry(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir", "UbisoftGameLauncher.exe")
              ?? TryRegistry(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir", "Ubisoft Game Launcher.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFilesX86, @"Ubisoft\Ubisoft Game Launcher\UbisoftGameLauncher.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFilesX86, @"Ubisoft\Ubisoft Game Launcher\Ubisoft Game Launcher.exe"),
        // Battle.net (Blizzard): launcher lives in LocalAppData or Program Files
        () => TryRegistry(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net", "InstallPath", "Battle.net Launcher.exe")
              ?? TryPath(Environment.SpecialFolder.LocalApplicationData, @"Battle.net\Battle.net Launcher.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFilesX86, @"Battle.net\Battle.net Launcher.exe")
              ?? TryPath(Environment.SpecialFolder.ProgramFiles, @"Battle.net\Battle.net Launcher.exe"),
        // Rockstar Games Launcher
        () => TryPath(Environment.SpecialFolder.ProgramFiles, @"Rockstar Games\Launcher\Launcher.exe"),
        // Amazon Games: installs to system drive root by default (C:\Amazon Games), not Program Files
        () => TryRegistry(@"SOFTWARE\WOW6432Node\Amazon Games", "InstallPath", "Amazon Games.exe")
              ?? TrySystemDrivePath(@"Amazon Games\Application\Amazon Games.exe"),
    ];

    private static string? TryRegistry(string keyPath, string valueName, string? relativePath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is not string regBase)
                return null;
            var path = relativePath != null ? Path.Combine(regBase.TrimEnd('\\', '/'), relativePath) : regBase;
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryPath(Environment.SpecialFolder folder, string relative)
    {
        var path = Path.Combine(Environment.GetFolderPath(folder), relative);
        return File.Exists(path) ? path : null;
    }

    private static string? TrySystemDrivePath(string relative)
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root))
            return null;
        var path = Path.Combine(root, relative);
        return File.Exists(path) ? path : null;
    }

    // Paths relative to %LOCALAPPDATA% for launchers that install per-user
    private static readonly string[] PerAccountLauncherRelativePaths =
    [
        @"Programs\Playnite\Playnite.DesktopApp.exe",
        @"Playnite\Playnite.DesktopApp.exe",
        @"GOG Galaxy\GalaxyClient.exe",
    ];

    private static string? TryProfilePath(string accountSid, string relativeToLocalAppData)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{accountSid}");
            if (key?.GetValue("ProfileImagePath") is not string profileRoot)
                return null;
            var fullPath = Path.Combine(profileRoot, "AppData", "Local", relativeToLocalAppData);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private bool _autoDetected;

    /// <param name="setLauncherPaths">Callback invoked by <see cref="Collect"/> with the final path list.</param>
    /// <param name="discoveryService">Used by the Discover button to find installed apps.</param>
    /// <param name="getSid">
    /// Optional callback invoked on activation to get the target account SID.
    /// When non-null and returns a SID, launchers installed in that account's user profile
    /// (e.g., Playnite) are detected in addition to system-wide ones.
    /// Returns null for new accounts (profile not yet created) — only system-wide detection runs.
    /// </param>
    public GamingLaunchersStep(
        Action<List<string>> setLauncherPaths,
        IShortcutDiscoveryService discoveryService,
        Func<string?>? getSid = null)
    {
        _setLauncherPaths = setLauncherPaths;
        _discoveryService = discoveryService;
        _getAccountSid = getSid;
        BuildContent();
    }

    public override string StepTitle => "Game Launchers";

    public override void OnActivated() => AutoDetectLaunchers();

    public override string? Validate()
    {
        if (_editor.GetItems().Count == 0)
            return "Please add at least one game launcher executable.";
        return null;
    }

    public override void Collect()
    {
        _setLauncherPaths(_editor.GetItems().ToList());
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _editor = new FolderListEditor();
        _editor.Initialize(
            "Specify the executables for your game launchers. Known launchers are detected automatically.",
            FolderBrowseDialogType.ExecutableFile,
            browseDialogTitle: "Select Game Launcher Executable");

        _discoverButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Text = "Discover…",
            ToolTipText = "Discover installed apps",
            Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x22, 0x6A, 0xB8))
        };
        _discoverButton.Click += OnDiscoverLauncher;
        _editor.AddExtraToolbarButton(_discoverButton);

        _ctxDiscover = new ToolStripMenuItem("Discover…")
        {
            Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x22, 0x6A, 0xB8), 16)
        };
        _ctxDiscover.Click += OnDiscoverLauncher;
        _editor.AddExtraContextMenuItem(_ctxDiscover);

        Controls.Add(_editor);
        ResumeLayout(false);
    }

    private void AutoDetectLaunchers()
    {
        if (_autoDetected)
            return;
        _autoDetected = true;

        foreach (var detect in KnownLauncherDetectors)
        {
            var path = detect();
            if (path != null)
                _editor.AddItem(path);
        }

        var accountSid = _getAccountSid?.Invoke();
        if (!string.IsNullOrEmpty(accountSid))
        {
            foreach (var relPath in PerAccountLauncherRelativePaths)
            {
                var path = TryProfilePath(accountSid, relPath);
                if (path != null)
                    _editor.AddItem(path);
            }
        }
    }

    private async void OnDiscoverLauncher(object? sender, EventArgs e)
    {
        _discoverButton.Enabled = false;
        _ctxDiscover.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var apps = await Task.Run(() => _discoveryService.DiscoverApps());
            if (IsDisposed) return;

            using var dlg = new AppDiscoveryDialog(apps);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedPath != null)
                _editor.AddItem(dlg.SelectedPath);
        }
        finally
        {
            if (!IsDisposed)
            {
                Cursor = Cursors.Default;
                _discoverButton.Enabled = true;
                _ctxDiscover.Enabled = true;
            }
        }
    }
}

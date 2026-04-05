using System.ComponentModel;
using Microsoft.Win32;
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

    private Label _infoLabel = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _addButton = null!;
    private ToolStripButton _removeButton = null!;
    private ListBox _launcherListBox = null!;
    private ContextMenuStrip _contextMenu = null!;
    private ToolStripMenuItem _ctxAdd = null!;
    private ToolStripMenuItem _ctxRemove = null!;

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

    private readonly Func<string?>? _getAccountSid;
    private bool _autoDetected;

    /// <param name="setLauncherPaths">Callback invoked by <see cref="Collect"/> with the final path list.</param>
    /// <param name="getSid">
    /// Optional callback invoked on activation to get the target account SID.
    /// When non-null and returns a SID, launchers installed in that account's user profile
    /// (e.g., Playnite) are detected in addition to system-wide ones.
    /// Returns null for new accounts (profile not yet created) — only system-wide detection runs.
    /// </param>
    public GamingLaunchersStep(Action<List<string>> setLauncherPaths, Func<string?>? getSid = null)
    {
        _setLauncherPaths = setLauncherPaths;
        _getAccountSid = getSid;
        BuildContent();
    }

    public override string StepTitle => "Game Launchers";

    public override void OnActivated() => AutoDetectLaunchers();

    public override string? Validate()
    {
        if (_launcherListBox.Items.Count == 0)
            return "Please add at least one game launcher executable.";
        return null;
    }

    public override void Collect()
    {
        _setLauncherPaths(_launcherListBox.Items.Cast<string>().ToList());
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = "Specify the executables for your game launchers. Known launchers are detected automatically.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        TrackWrappingLabel(_infoLabel);

        _toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            BackColor = Color.White
        };
        _addButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Add Launcher…",
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22))
        };
        _addButton.Click += OnAddLauncher;

        _removeButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Remove",
            Enabled = false,
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33))
        };
        _removeButton.Click += OnRemoveLauncher;
        _toolStrip.Items.AddRange(_addButton, _removeButton);

        _launcherListBox = new ListBox
        {
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false
        };
        _launcherListBox.SelectedIndexChanged += (_, _) => UpdateButtons();
        _launcherListBox.MouseDown += OnMouseDown;
        _launcherListBox.KeyDown += OnKeyDown;

        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem("Add Launcher…")
        {
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16)
        };
        _ctxAdd.Click += OnAddLauncher;
        _ctxRemove = new ToolStripMenuItem("Remove")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16)
        };
        _ctxRemove.Click += OnRemoveLauncher;
        _contextMenu.Items.AddRange(_ctxAdd, _ctxRemove);
        _contextMenu.Opening += OnContextMenuOpening;
        _launcherListBox.ContextMenuStrip = _contextMenu;

        // Fill first, then Top items (last added = topmost in DockStyle.Top stack).
        Controls.Add(_launcherListBox);
        Controls.Add(_toolStrip);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }

    private void AutoDetectLaunchers()
    {
        if (_autoDetected)
            return;
        _autoDetected = true;

        var existing = _launcherListBox.Items.Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // System-wide launchers installed in Program Files
        foreach (var detect in KnownLauncherDetectors)
        {
            var path = detect();
            if (path != null && existing.Add(path))
                _launcherListBox.Items.Add(path);
        }

        // Per-account launchers installed in the account's user profile (e.g. Playnite)
        var accountSid = _getAccountSid?.Invoke();
        if (!string.IsNullOrEmpty(accountSid))
        {
            foreach (var relPath in PerAccountLauncherRelativePaths)
            {
                var path = TryProfilePath(accountSid, relPath);
                if (path != null && existing.Add(path))
                    _launcherListBox.Items.Add(path);
            }
        }
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

    private void OnAddLauncher(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Title = "Select Game Launcher Executable";
        dlg.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
        dlg.CheckFileExists = true;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var path = dlg.FileName;
            if (!_launcherListBox.Items.Contains(path))
                _launcherListBox.Items.Add(path);
        }
    }

    private void OnRemoveLauncher(object? sender, EventArgs e)
    {
        if (_launcherListBox.SelectedIndex >= 0)
            _launcherListBox.Items.RemoveAt(_launcherListBox.SelectedIndex);
    }

    private void UpdateButtons()
    {
        _removeButton.Enabled = _launcherListBox.SelectedIndex >= 0;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _launcherListBox.SelectedIndex = _launcherListBox.IndexFromPoint(e.Location);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _launcherListBox.SelectedIndex >= 0)
            OnRemoveLauncher(sender, e);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _launcherListBox.SelectedIndex < 0;
        _ctxRemove.Visible = _launcherListBox.SelectedIndex >= 0;
    }
}
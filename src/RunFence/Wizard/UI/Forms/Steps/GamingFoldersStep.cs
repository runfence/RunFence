using System.ComponentModel;
using Microsoft.Win32;
using RunFence.UI;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for specifying game install folders for the Gaming Account template.
/// Grants the gaming account "Allow All including Owner" rights on these folders so it can
/// install and update games there.
/// </summary>
public class GamingFoldersStep : WizardStepPage
{
    private readonly Action<List<string>> _setGameFolders;

    private Label _infoLabel = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _addButton = null!;
    private ToolStripButton _removeButton = null!;
    private ListBox _folderListBox = null!;
    private Label _tipLabel = null!;
    private ContextMenuStrip _contextMenu = null!;
    private ToolStripMenuItem _ctxAdd = null!;
    private ToolStripMenuItem _ctxRemove = null!;

    public GamingFoldersStep(Action<List<string>> setGameFolders)
    {
        _setGameFolders = setGameFolders;
        BuildContent();
    }

    public override string StepTitle => "Game Install Folders";

    public override void OnActivated() => AutoDetectFolders();

    public override string? Validate() => null;

    public override void Collect()
    {
        _setGameFolders(_folderListBox.Items.Cast<string>().ToList());
    }

    // Folder names checked at the root of every fixed drive
    private static readonly string[] KnownLibraryFolderNames =
    [
        "SteamLibrary", // Steam
        "XboxGames", // Xbox / Microsoft Store
        "GOG Games", // GOG Galaxy
        "Epic Games", // Epic Games Launcher
        "EA Games", // EA App
        "Origin Games", // EA Origin (legacy)
        "Blizzard Games", // Battle.net
        "Rockstar Games", // Rockstar Games Launcher
        "Amazon Games", // Amazon Games
    ];

    private bool _autoDetected;

    private void AutoDetectFolders()
    {
        if (_autoDetected)
            return;
        _autoDetected = true;

        var existing = _folderListBox.Items.Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;
                foreach (var name in KnownLibraryFolderNames)
                {
                    var path = Path.Combine(drive.RootDirectory.FullName, name);
                    if (Directory.Exists(path) && existing.Add(path))
                        _folderListBox.Items.Add(path);
                }
            }
        }
        catch
        {
        }

        foreach (var path in GetSteamLibraryPaths())
        {
            if (existing.Add(path))
                _folderListBox.Items.Add(path);
        }
    }

    /// <summary>
    /// Reads Steam's libraryfolders.vdf to discover all configured Steam library paths,
    /// including those with custom names that wouldn't be found by the drive root scan.
    /// </summary>
    private static IEnumerable<string> GetSteamLibraryPaths()
    {
        string? steamPath = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            steamPath = key?.GetValue("InstallPath") as string;
        }
        catch
        {
        }

        if (steamPath == null)
            yield break;

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            yield break;

        IEnumerable<string> lines;
        try
        {
            lines = File.ReadAllLines(vdfPath);
        }
        catch
        {
            yield break;
        }

        foreach (var line in lines)
        {
            // VDF format: "path"    "D:\\Games\\Steam" (tabs or spaces between key and value)
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                continue;
            var valueStart = trimmed.IndexOf('"', 6);
            if (valueStart < 0)
                continue;
            var valueEnd = trimmed.IndexOf('"', valueStart + 1);
            if (valueEnd < 0)
                continue;
            var path = trimmed.Substring(valueStart + 1, valueEnd - valueStart - 1)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
            if (Directory.Exists(path))
                yield return path;
        }
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = "Add folders where games will be installed. The gaming account will have full access (including ownership) " +
                   "on these folders so launchers can install and update games.",
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
            ToolTipText = "Add Folder…",
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22))
        };
        _addButton.Click += OnAddFolder;

        _removeButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Remove",
            Enabled = false,
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33))
        };
        _removeButton.Click += OnRemoveFolder;
        _toolStrip.Items.AddRange(_addButton, _removeButton);

        _folderListBox = new ListBox
        {
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false
        };
        _folderListBox.SelectedIndexChanged += (_, _) => UpdateButtons();
        _folderListBox.MouseDown += OnMouseDown;
        _folderListBox.KeyDown += OnKeyDown;

        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem("Add Folder…")
        {
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16)
        };
        _ctxAdd.Click += OnAddFolder;
        _ctxRemove = new ToolStripMenuItem("Remove")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16)
        };
        _ctxRemove.Click += OnRemoveFolder;
        _contextMenu.Items.AddRange(_ctxAdd, _ctxRemove);
        _contextMenu.Opening += OnContextMenuOpening;
        _folderListBox.ContextMenuStrip = _contextMenu;

        _tipLabel = new Label
        {
            Text = "Tip: Known game library folders are auto-detected from all drives. Add more folders manually if needed.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 4, 0, 0)
        };
        TrackWrappingLabel(_tipLabel);

        // Fill first, then Top/Bottom items (last added = topmost in DockStyle.Top stack).
        Controls.Add(_folderListBox);
        Controls.Add(_tipLabel);
        Controls.Add(_toolStrip);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }

    private void OnAddFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select game install folder";
        dlg.UseDescriptionForTitle = true;
        dlg.ShowNewFolderButton = true;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var path = dlg.SelectedPath;
            if (!_folderListBox.Items.Contains(path))
                _folderListBox.Items.Add(path);
        }
    }

    private void OnRemoveFolder(object? sender, EventArgs e)
    {
        if (_folderListBox.SelectedIndex >= 0)
            _folderListBox.Items.RemoveAt(_folderListBox.SelectedIndex);
    }

    private void UpdateButtons()
    {
        _removeButton.Enabled = _folderListBox.SelectedIndex >= 0;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _folderListBox.SelectedIndex = _folderListBox.IndexFromPoint(e.Location);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _folderListBox.SelectedIndex >= 0)
            OnRemoveFolder(sender, e);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _folderListBox.SelectedIndex < 0;
        _ctxRemove.Visible = _folderListBox.SelectedIndex >= 0;
    }
}
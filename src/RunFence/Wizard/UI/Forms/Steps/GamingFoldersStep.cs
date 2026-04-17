using Microsoft.Win32;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for specifying game install folders for the Gaming Account template.
/// Grants the gaming account "Allow All including Owner" rights on these folders so it can
/// install and update games there.
/// </summary>
public class GamingFoldersStep : WizardStepPage
{
    private readonly Action<List<string>> _setGameFolders;

    private FolderListEditor _editor = null!;
    private Label _tipLabel = null!;

    public GamingFoldersStep(Action<List<string>> setGameFolders)
    {
        _setGameFolders = setGameFolders;
        BuildContent();
    }

    public override string StepTitle => "Game Install Folders";

    public override void OnActivated() => _ = AutoDetectFoldersAsync();

    public override string? Validate() => null;

    public override void Collect()
    {
        _setGameFolders(_editor.GetItems().ToList());
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

    // System-protected roots that must never be suggested as game library folders.
    // Steam's VDF may list its own install directory (typically under Program Files) as a library.
    private static readonly string[] SystemProtectedRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows)
    ];

    private bool _autoDetected;

    private async Task AutoDetectFoldersAsync()
    {
        if (_autoDetected)
            return;
        _autoDetected = true;

        _tipLabel.Text = "Scanning drives...";

        var paths = await Task.Run(ScanForGameFolders);

        if (IsDisposed)
            return;

        foreach (var path in paths)
            _editor.AddItem(path);

        _tipLabel.Text = "Tip: Known game library folders are auto-detected from all drives. Add more folders manually if needed.";
    }

    private static List<string> ScanForGameFolders()
    {
        var result = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;
                foreach (var name in KnownLibraryFolderNames)
                {
                    var path = Path.Combine(drive.RootDirectory.FullName, name);
                    if (Directory.Exists(path))
                        result.Add(path);
                }
            }
        }
        catch
        {
        }

        foreach (var path in GetSteamLibraryPaths())
            result.Add(path);

        return result;
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
            if (Directory.Exists(path) && !IsSystemProtectedPath(path))
                yield return path;
        }
    }

    /// <summary>
    /// Returns true if the path is under ProgramFiles, ProgramFilesX86, or Windows.
    /// </summary>
    private static bool IsSystemProtectedPath(string path)
    {
        foreach (var root in SystemProtectedRoots)
        {
            if (string.IsNullOrEmpty(root))
                continue;
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _editor = new FolderListEditor();
        _editor.Initialize(
            "Add folders where games will be installed. The gaming account will have full access (including ownership) " +
            "on these folders so launchers can install and update games.",
            FolderBrowseDialogType.FolderWithCreate,
            browseDialogTitle: "Select game install folder");

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

        // Fill first, then Bottom items.
        Controls.Add(_editor);
        Controls.Add(_tipLabel);
        ResumeLayout(false);
    }
}

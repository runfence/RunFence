using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

/// <summary>
/// Reads and writes taskbar settings, including pinned shortcuts.
/// <para>
/// <b>Known limitation:</b> Pinned taskbar shortcuts are stored as <c>.lnk</c> files whose
/// internal target paths embed the source account's profile path. When transferring across
/// accounts, the binary blobs in <c>Favorites</c> and <c>FavoritesResolve</c> are patched to
/// replace the source profile path with the target profile path, but shortcuts that point to
/// per-user locations outside the standard profile root (e.g., per-user AppData shortcuts to
/// Store apps) may still contain stale source account paths and produce broken taskbar items on
/// the target account.
/// </para>
/// <para>
/// COM calls (<c>WScript.Shell</c> via <c>CreateShortcut</c>) require an STA thread.
/// <c>[STAThread]</c> on <c>Main</c> in <c>Program.cs</c> provides STA for the entire process.
/// This is less robust than spinning a dedicated STA thread (as in SecurityScanner), but
/// sufficient for a single-threaded CLI tool that never spawns background COM callers.
/// </para>
/// </summary>
public class TaskbarSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast, IUserProfileFilter userProfileFilter) : ISettingsIO
{
    public TaskbarSettings Read()
    {
        var taskbar = new TaskbarSettings();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
            taskbar.SourceProfilePath = userProfile;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegExplorerAdvanced);
            if (key == null)
                return;
            taskbar.SmallIcons = key.GetValue("TaskbarSmallIcons") as int?;
            taskbar.ShowTaskViewButton = key.GetValue("ShowTaskViewButton") as int?;
            taskbar.TaskbarAlignment = key.GetValue("TaskbarAl") as int?;
            taskbar.ShowWidgets = key.GetValue("TaskbarDa") as int?;
            taskbar.ButtonCombine = key.GetValue("TaskbarGlomLevel") as int?;
            taskbar.MultiMonitorButtonCombine = key.GetValue("MMTaskbarGlomLevel") as int?;
            taskbar.VirtualDesktopTaskbarFilter = key.GetValue("VirtualDesktopTaskbarFilter") as int?;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegTaskband);
            if (key == null)
                return;
            taskbar.Favorites = key.GetValue("Favorites") as byte[];
            taskbar.FavoritesResolve = key.GetValue("FavoritesResolve") as byte[];
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegSearch);
            taskbar.SearchboxTaskbarMode = key?.GetValue("SearchboxTaskbarMode") as int?;
        }, "reading");
        safe.Try(() =>
        {
            var pinnedFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (!Directory.Exists(pinnedFolder))
                return;

            var profilePaths = userProfileFilter.GetUserProfilePaths();
            var lnkFiles = Directory.GetFiles(pinnedFolder, "*.lnk");
            if (lnkFiles.Length == 0)
                return;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null)
                    return;
                var shortcuts = new List<string>();
                var shortcutFiles = new Dictionary<string, byte[]>();
                foreach (var lnkPath in lnkFiles)
                {
                    safe.Try(() =>
                    {
                        dynamic lnk = shell.CreateShortcut(lnkPath);
                        try
                        {
                            var target = lnk.TargetPath as string;
                            // Skip Store apps (no target path), user-profile-specific targets,
                            // and UWP package paths (Program Files\WindowsApps\)
                            if (string.IsNullOrEmpty(target))
                                return;
                            if (userProfileFilter.ContainsUserProfilePath(target, profilePaths))
                                return;
                            if (userProfileFilter.ContainsWindowsAppsPath(target))
                                return;
                            var fileName = Path.GetFileName(lnkPath);
                            shortcuts.Add(fileName);
                            // Also capture the raw .lnk bytes so the target account can recreate
                            // the shortcut file even without access to the source account's AppData.
                            safe.Try(() => shortcutFiles[fileName] = File.ReadAllBytes(lnkPath), "reading");
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(lnk);
                        }
                    }, "reading");
                }

                if (shortcuts.Count > 0)
                    taskbar.PinnedShortcuts = shortcuts;
                if (shortcutFiles.Count > 0)
                    taskbar.PinnedShortcutFiles = shortcutFiles;
            }
            finally
            {
                if (shell != null)
                    Marshal.ReleaseComObject(shell);
            }
        }, "reading");
        return taskbar;
    }

    public void Write(TaskbarSettings taskbar)
    {
        bool changed = false;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegExplorerAdvanced);

            void Set(string name, int? val)
            {
                if (val.HasValue)
                {
                    key.SetValue(name, val.Value, RegistryValueKind.DWord);
                    changed = true;
                }
            }

            Set("TaskbarSmallIcons", taskbar.SmallIcons);
            Set("ShowTaskViewButton", taskbar.ShowTaskViewButton);
            Set("TaskbarAl", taskbar.TaskbarAlignment);
            Set("TaskbarDa", taskbar.ShowWidgets);
            Set("TaskbarGlomLevel", taskbar.ButtonCombine);
            Set("MMTaskbarGlomLevel", taskbar.MultiMonitorButtonCombine);
            Set("VirtualDesktopTaskbarFilter", taskbar.VirtualDesktopTaskbarFilter);
        }, "writing");
        safe.Try(() =>
        {
            if (taskbar.Favorites == null && taskbar.FavoritesResolve == null)
                return;

            var targetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(targetProfile))
                return;

            byte[]? favorites = taskbar.Favorites;
            byte[]? favoritesResolve = taskbar.FavoritesResolve;

            var sourceProfile = taskbar.SourceProfilePath;
            if (!string.IsNullOrEmpty(sourceProfile) &&
                !string.Equals(sourceProfile, targetProfile, StringComparison.OrdinalIgnoreCase))
            {
                // Cross-account import: patch binary blobs to replace the source user's profile path
                // with the target user's profile path. Both Favorites and FavoritesResolve embed
                // CountedString-prefixed paths to .lnk files in the source user's TaskBar folder.
                favorites = PatchProfilePath(favorites, sourceProfile, targetProfile);
                favoritesResolve = PatchProfilePath(favoritesResolve, sourceProfile, targetProfile);
            }
            else if (string.IsNullOrEmpty(sourceProfile))
            {
                // Legacy JSON (no SourceProfilePath): fall back to ownership check — if neither blob
                // contains the current user's profile path it was exported by a different account and
                // cannot be safely patched without knowing the source path.
                bool ownedByCurrentUser =
                    ContainsPathUtf16(taskbar.Favorites, targetProfile) ||
                    ContainsPathUtf16(taskbar.FavoritesResolve, targetProfile);
                if (!ownedByCurrentUser)
                    return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegTaskband);
            if (favorites != null)
                key.SetValue("Favorites", favorites, RegistryValueKind.Binary);
            if (favoritesResolve != null)
                key.SetValue("FavoritesResolve", favoritesResolve, RegistryValueKind.Binary);
            changed = true;
        }, "writing");
        safe.Try(() =>
        {
            var taskBarFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

            if (taskbar.PinnedShortcutFiles != null)
            {
                // Write .lnk files captured at export time into the target account's TaskBar folder.
                // When transferring across accounts, patch the source profile path embedded in the
                // .lnk binary content with the target profile path so shortcuts remain functional.
                Directory.CreateDirectory(taskBarFolder);
                var targetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var sourceProfile = taskbar.SourceProfilePath;
                bool shouldPatch = !string.IsNullOrEmpty(sourceProfile) &&
                                   !string.IsNullOrEmpty(targetProfile) &&
                                   !string.Equals(sourceProfile, targetProfile, StringComparison.OrdinalIgnoreCase);
                foreach (var (fileName, content) in taskbar.PinnedShortcutFiles)
                {
                    safe.Try(() =>
                    {
                        var patched = shouldPatch
                            ? PatchProfilePath(content, sourceProfile!, targetProfile) ?? content
                            : content;
                        File.WriteAllBytes(Path.Combine(taskBarFolder, fileName), patched);
                    }, "writing");
                }

                changed = true;
            }
            else if (taskbar.PinnedShortcuts != null)
            {
                // Legacy JSON (no PinnedShortcutFiles): log missing shortcuts for diagnostics.
                if (!Directory.Exists(taskBarFolder))
                    return;
                foreach (var name in taskbar.PinnedShortcuts)
                {
                    if (!File.Exists(Path.Combine(taskBarFolder, name)))
                        Console.Error.WriteLine($"Warning: pinned shortcut not found: {name}");
                }
            }
        }, "writing");
        safe.Try(() =>
        {
            if (taskbar.SearchboxTaskbarMode.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegSearch);
                key.SetValue("SearchboxTaskbarMode", taskbar.SearchboxTaskbarMode.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        if (changed)
            broadcast.Broadcast();
    }

    // Patches all occurrences of sourceProfile (UTF-16 LE) in the binary blob with targetProfile,
    // updating the preceding 2-byte LE CountedString character-count field when detected.
    // Returns the patched blob, or the original blob unchanged when no match is found.
    private static byte[]? PatchProfilePath(byte[]? blob, string sourceProfile, string targetProfile)
    {
        if (blob == null)
            return null;

        var sourceBytes = Encoding.Unicode.GetBytes(sourceProfile);
        var targetBytes = Encoding.Unicode.GetBytes(targetProfile);
        int bytesDelta = targetBytes.Length - sourceBytes.Length; // always a multiple of 2
        int charsDelta = bytesDelta / 2;

        // Quick exit when source is not present at all (common same-user path).
        if (IndexOfBytes(blob, sourceBytes, 0) < 0)
            return blob;

        var result = new List<byte>(blob.Length + Math.Max(0, bytesDelta) * 8);
        int pos = 0;

        while (pos < blob.Length)
        {
            int matchPos = IndexOfBytes(blob, sourceBytes, pos);
            if (matchPos < 0)
            {
                result.AddRange(new ArraySegment<byte>(blob, pos, blob.Length - pos));
                break;
            }

            int bytesToCopy = matchPos - pos;
            result.AddRange(new ArraySegment<byte>(blob, pos, bytesToCopy));

            // When the path length changes we try to update the 2-byte LE CountedString length
            // field that immediately precedes the string in the blob (FavoritesResolve uses this
            // format; Favorites may too). The field encodes the character count of the full string
            // that starts with sourceProfile, so it must be adjusted by charsDelta.
            // Guard: we need at least 2 bytes copied from blob in this iteration so the last 2 bytes
            // in result are blob[matchPos-2] and blob[matchPos-1] (the actual count field), not bytes
            // from a prior replacement.
            if (bytesDelta != 0 && matchPos >= 2 && bytesToCopy >= 2)
            {
                int oldCharCount = blob[matchPos - 2] | (blob[matchPos - 1] << 8);
                int newCharCount = oldCharCount + charsDelta;
                // Sanity: old count must cover at least the source profile length and be plausible.
                if (oldCharCount >= sourceProfile.Length && oldCharCount <= 4096 &&
                    newCharCount is > 0 and <= 4096)
                {
                    int ri = result.Count;
                    result[ri - 2] = (byte)(newCharCount & 0xFF);
                    result[ri - 1] = (byte)((newCharCount >> 8) & 0xFF);
                }
            }

            result.AddRange(targetBytes);
            pos = matchPos + sourceBytes.Length;
        }

        return result.ToArray();
    }

    private static int IndexOfBytes(byte[] data, byte[] pattern, int startPos)
    {
        int end = data.Length - pattern.Length;
        for (int i = startPos; i <= end; i++)
        {
            // LINQ in loop is acceptable — no highload scenarios in this tool.
            if (!pattern.Where((t, j) => data[i + j] != t).Any())
                return i;
        }

        return -1;
    }

    // Used only for the legacy fallback path (JSON without SourceProfilePath).
    private static bool ContainsPathUtf16(byte[]? data, string path)
    {
        if (data == null || data.Length < 2 || string.IsNullOrEmpty(path))
            return false;
        var text = Encoding.Unicode.GetString(data);
        return text.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Taskbar = Read();
    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Taskbar != null) Write(s.Taskbar); }
}

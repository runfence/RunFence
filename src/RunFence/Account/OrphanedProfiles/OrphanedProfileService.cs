using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Account.OrphanedProfiles;

public class OrphanedProfileService(
    ILoggingService log,
    NTTranslateApi ntTranslate,
    IGroupPolicyScriptHelper gpHelper,
    IProfileSizeCalculator profileSizeCalculator,
    IProfileDirectoryRemovalService profileDirectoryRemovalService,
    string? usersDir = null,
    string? systemDir = null)
    : IOrphanedProfileService
{
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default",
        "All Users",
        "Default User",
        "Public",
        "DefaultAppPool"
    };

    private readonly string _usersDir = Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(
            usersDir ?? (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + @"\Users"));

    public List<OrphanedProfile> GetOrphanedProfiles()
    {
        if (!Directory.Exists(_usersDir))
            return [];

        var registeredByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, path) in GetProfileRegistryEntries())
            registeredByPath[path] = sid;

        var result = new List<OrphanedProfile>();
        foreach (var dir in Directory.GetDirectories(_usersDir))
        {
            var fullDir = Path.GetFullPath(dir);

            if (registeredByPath.TryGetValue(fullDir, out var sid))
            {
                if (!AccountExists(sid))
                    result.Add(new OrphanedProfile(sid, fullDir));
            }
            else if (!ExcludedNames.Contains(Path.GetFileName(fullDir)))
            {
                result.Add(new OrphanedProfile(null, fullDir));
            }
        }

        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.ProfilePath, b.ProfilePath));
        return result;
    }

    public long GetProfileSizeBytes(string profilePath, IProgress<long>? progress, CancellationToken cancellationToken)
        => profileSizeCalculator.CalculateSizeBytes(profilePath, progress, cancellationToken);

    public (List<string> Deleted, List<(string Path, string Error)> Failed) DeleteProfiles(IEnumerable<OrphanedProfile> profiles)
    {
        var deleted = new List<string>();
        var failed = new List<(string, string)>();

        foreach (var profile in profiles)
        {
            if (!IsAllowedPath(profile.ProfilePath))
            {
                failed.Add((profile.ProfilePath, "Path rejected for safety"));
                continue;
            }

            var renamedPath = profile.ProfilePath + ".deleted";
            bool renamed = false;
            try
            {
                if (Directory.Exists(renamedPath))
                    DeleteSafe(renamedPath);

                Directory.Move(profile.ProfilePath, renamedPath);
                renamed = true;

                profileDirectoryRemovalService.RemoveMovedProfileDirectory(renamedPath);
                deleted.Add(profile.ProfilePath);
                log.Info($"Moved orphaned profile to Recycle Bin: {profile.ProfilePath}");

                if (profile.Sid != null)
                {
                    RemoveProfileRegistryKey(profile.Sid);
                    CleanupLogonScripts(profile.Sid);
                }
            }
            catch (Exception ex)
            {
                if (renamed)
                {
                    try
                    {
                        Directory.Move(renamedPath, profile.ProfilePath);
                    }
                    catch (Exception undoEx)
                    {
                        log.Warn($"Failed to restore '{renamedPath}' to '{profile.ProfilePath}' after partial deletion. " +
                                  $"The profile directory may be in an inconsistent state. " +
                                  $"Rename-back error: {undoEx.Message}. Original deletion error: {ex.Message}");
                    }
                }

                failed.Add((profile.ProfilePath, ex.Message));
                log.Warn($"Failed to move orphaned profile '{profile.ProfilePath}' to the Recycle Bin: {ex.Message}");
            }
        }

        return (deleted, failed);
    }

    protected virtual IEnumerable<(string Sid, string ProfilePath)> GetProfileRegistryEntries()
    {
        var entries = new List<(string, string)>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PathConstants.ProfileListRegistryKey);
            if (key == null)
                return entries;

            foreach (var sidName in key.GetSubKeyNames())
            {
                if (sidName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (sidName.EndsWith(".deleted", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lastDot = sidName.LastIndexOf('.');
                var suffix = lastDot > 0 ? sidName[(lastDot + 1)..] : "";
                if (suffix.Length > 0 &&
                    suffix.All(char.IsDigit) &&
                    sidName[..lastDot].EndsWith(".deleted", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var subKey = key.OpenSubKey(sidName);
                var raw = subKey?.GetValue("ProfileImagePath") as string;
                if (string.IsNullOrEmpty(raw))
                    continue;

                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(raw);
                    entries.Add((sidName, Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded))));
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to read profile list from registry: {ex.Message}");
        }

        return entries;
    }

    protected virtual bool AccountExists(string sidString)
    {
        try
        {
            ntTranslate.TranslateName(sidString);
            return true;
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            log.Warn($"Cannot verify account existence for SID '{sidString}': {ex.Message}");
            return true;
        }
    }

    private static void DeleteSafe(string path)
    {
        var attrs = File.GetAttributes(path);

        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.System)) != 0)
                File.SetAttributes(path, attrs & ~(FileAttributes.ReadOnly | FileAttributes.System));

            if ((attrs & FileAttributes.Directory) != 0)
                Directory.Delete(path);
            else
                File.Delete(path);

            return;
        }

        if ((attrs & FileAttributes.Directory) != 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                DeleteSafe(entry);

            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.System)) != 0)
                File.SetAttributes(path, FileAttributes.Normal);

            Directory.Delete(path);
        }
        else
        {
            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.System)) != 0)
                File.SetAttributes(path, FileAttributes.Normal);

            File.Delete(path);
        }
    }

    private void RemoveProfileRegistryKey(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PathConstants.ProfileListRegistryKey, writable: true);
            if (key == null)
                return;

            key.DeleteSubKeyTree(sid, throwOnMissingSubKey: false);
            key.DeleteSubKeyTree(sid + "_Classes", throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remove registry key for SID '{sid}': {ex.Message}");
        }
    }

    public void CleanupLogonScripts(string sid)
    {
        try
        {
            gpHelper.SetLoginBlocked(sid, false);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remove logon script entry for SID '{sid}': {ex.Message}");
        }

        try
        {
            var sysDir = systemDir ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
            var gpUserSidDir = Path.Combine(sysDir, "GroupPolicyUsers", sid);
            if (Directory.Exists(gpUserSidDir))
            {
                Directory.Delete(gpUserSidDir, recursive: true);
                log.Info($"Deleted GroupPolicyUsers directory for SID '{sid}'");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to delete GroupPolicyUsers directory for SID '{sid}': {ex.Message}");
        }
    }

    private bool IsAllowedPath(string path)
    {
        try
        {
            var normalized = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(normalized);
            return string.Equals(parent, _usersDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.TrayIcon;

public record StartMenuEntry(string Name, string ExePath, string AccountSid, string? Subfolder);

public class StartMenuDiscoveryService(ISidResolver sidResolver)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".com", ".bat", ".cmd", ".ps1" };

    public List<StartMenuEntry> Scan(IReadOnlyList<CredentialEntry> credentials, IReadOnlySet<(string ExePath, string AccountSid)> existingApps)
    {
        var results = new List<StartMenuEntry>();
        var seen = new HashSet<(string sid, string exePath)>(CaseInsensitiveTupleComparer.Instance);

        var currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd('\\', '/');
        var publicTargets = CollectPublicTargets();
        var interactiveUserCredential = credentials.FirstOrDefault(c => c.IsInteractiveUser);
        var interactiveTargets = interactiveUserCredential != null
            ? CollectUserTargets(interactiveUserCredential, currentUserProfile)
            : [];

        foreach (var credential in credentials)
        {
            if (credential is { IsCurrentAccount: false, IsInteractiveUser: false, EncryptedPassword.Length: 0 })
                continue;

            var profilePath = credential.IsCurrentAccount ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : sidResolver.TryGetProfilePath(credential.Sid);

            var profilePrefix = profilePath != null
                ? profilePath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar
                : null;

            var programsPath = sidResolver.TryGetStartMenuProgramsPath(credential.Sid, credential.IsCurrentAccount);
            var desktopPath = sidResolver.TryGetDesktopPath(credential.Sid, credential.IsCurrentAccount);

            // Scan directories: (path, subfolder-root or null-means-desktop)
            var scanDirs = new List<(string Dir, string? SubfolderRoot)>();
            if (programsPath != null && Directory.Exists(programsPath))
                scanDirs.Add((programsPath, programsPath));
            if (desktopPath != null && Directory.Exists(desktopPath))
                scanDirs.Add((desktopPath, null));

            foreach (var (dir, subfolderRoot) in scanDirs)
            {
                List<string> lnkFiles;
                try
                {
                    lnkFiles = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var lnkPath in lnkFiles)
                {
                    string? target;
                    try
                    {
                        (target, _) = ShortcutComHelper.GetShortcutTargetAndArgs(lnkPath);
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(target))
                        continue;

                    // WScript.Shell expands env vars (%LOCALAPPDATA%, %APPDATA%, etc.) using the
                    // current process environment (admin account). Re-root to the target user's
                    // actual profile so paths resolve correctly for icon loading and launching.
                    if (!credential.IsCurrentAccount && profilePath != null)
                    {
                        var targetProfile = profilePath.TrimEnd('\\', '/');
                        if (!string.Equals(currentUserProfile, targetProfile, StringComparison.OrdinalIgnoreCase) &&
                            target.StartsWith(currentUserProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            target = targetProfile + target[currentUserProfile.Length..];
                    }

                    if (!SupportedExtensions.Contains(Path.GetExtension(target)))
                        continue;
                    if (ShortcutComHelper.IsUninstallShortcut(lnkPath, target))
                        continue;
                    if (ShortcutComHelper.IsSystemExecutable(target))
                        continue;
                    if (existingApps.Contains((target, credential.Sid)))
                        continue;
                    if (!File.Exists(target))
                        continue;

                    var isInProfileFolder = profilePrefix != null &&
                                            target.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase);

                    var isCoveredByPublic = publicTargets.Contains(target);
                    var isCoveredByInteractiveUser = !credential.IsInteractiveUser && interactiveTargets.Contains(target);
                    var isCovered = isCoveredByPublic || isCoveredByInteractiveUser;

                    if (!isInProfileFolder && isCovered)
                        continue;

                    var key = (credential.Sid, target);
                    if (!seen.Add(key))
                        continue;

                    var name = Path.GetFileNameWithoutExtension(lnkPath);
                    string? subfolder = null;
                    if (subfolderRoot != null)
                    {
                        var parentDir = Path.GetDirectoryName(lnkPath);
                        if (parentDir != null &&
                            !string.Equals(parentDir, subfolderRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            subfolder = Path.GetRelativePath(subfolderRoot, parentDir);
                        }
                    }

                    results.Add(new StartMenuEntry(name, target, credential.Sid, subfolder));
                }
            }
        }

        return results;
    }

    private HashSet<string> CollectPublicTargets()
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTargets(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), targets);
        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        CollectTargets(commonStartMenu, targets);
        return targets;
    }

    private HashSet<string> CollectUserTargets(CredentialEntry credential, string currentUserProfile)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? profilePath = credential.IsCurrentAccount
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : sidResolver.TryGetProfilePath(credential.Sid);

        var desktopPath = sidResolver.TryGetDesktopPath(credential.Sid, credential.IsCurrentAccount);
        CollectTargets(desktopPath, targets, currentUserProfile, profilePath);
        var programsPath = sidResolver.TryGetStartMenuProgramsPath(credential.Sid, credential.IsCurrentAccount);
        if (programsPath != null)
            CollectTargets(Path.GetDirectoryName(programsPath), targets, currentUserProfile, profilePath);
        return targets;
    }

    private static void CollectTargets(string? dir, HashSet<string> targets,
        string? currentUserProfile = null, string? targetProfilePath = null)
    {
        if (dir == null || !Directory.Exists(dir))
            return;
        var normalizedTarget = targetProfilePath?.TrimEnd('\\', '/');
        var shouldReroute = currentUserProfile != null && normalizedTarget != null &&
                            !string.Equals(currentUserProfile, normalizedTarget, StringComparison.OrdinalIgnoreCase);
        try
        {
            foreach (var lnkPath in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var (target, _) = ShortcutComHelper.GetShortcutTargetAndArgs(lnkPath);
                    if (string.IsNullOrEmpty(target))
                        continue;
                    if (shouldReroute && target.StartsWith(currentUserProfile! + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                        target = normalizedTarget! + target[currentUserProfile!.Length..];
                    targets.Add(target);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.TrayIcon;

public record StartMenuEntry(string Name, string ExePath, string AccountSid, string? Subfolder);

/// <summary>
/// Captures all per-credential context needed to scan a single directory for start menu shortcuts.
/// </summary>
internal record ScanContext(
    CredentialEntry Credential,
    string CurrentUserProfile,
    string? ProfilePath,
    HashSet<string> PublicTargets,
    HashSet<string> InteractiveTargets,
    HashSet<(string sid, string exePath)> Seen,
    IReadOnlySet<(string ExePath, string AccountSid)> ExistingApps,
    List<StartMenuEntry> Results);

public class StartMenuDiscoveryService(IProfilePathResolver profilePathResolver, IShortcutComHelper shortcutHelper, ILoggingService log)
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
            {
                log.Info($"Skipping tray discovery for account {credential.Sid}: no stored password");
                continue;
            }

            var profilePath = credential.IsCurrentAccount ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : profilePathResolver.TryGetProfilePath(credential.Sid);

            var programsPath = profilePathResolver.TryGetStartMenuProgramsPath(credential.Sid, credential.IsCurrentAccount);
            var desktopPath = profilePathResolver.TryGetDesktopPath(credential.Sid, credential.IsCurrentAccount);

            var context = new ScanContext(credential, currentUserProfile, profilePath,
                publicTargets, interactiveTargets, seen, existingApps, results);

            // Scan directories: (path, subfolder-root or null-means-desktop)
            if (programsPath != null && Directory.Exists(programsPath))
                ProcessShortcutsInDirectory(programsPath, programsPath, context);
            if (desktopPath != null && Directory.Exists(desktopPath))
                ProcessShortcutsInDirectory(desktopPath, null, context);
        }

        return results;
    }

    private void ProcessShortcutsInDirectory(string dir, string? subfolderRoot, ScanContext ctx)
    {
        var profilePrefix = ctx.ProfilePath != null
            ? ctx.ProfilePath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar
            : null;
        List<string> lnkFiles;
        try
        {
            lnkFiles = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return;
        }

        foreach (var lnkPath in lnkFiles)
        {
            string? target;
            try
            {
                target = shortcutHelper.GetShortcutTargetAndArgs(lnkPath).Target;
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
            target = RerootToUserProfile(target, ctx.Credential.IsCurrentAccount, ctx.CurrentUserProfile, ctx.ProfilePath);

            if (!SupportedExtensions.Contains(Path.GetExtension(target)))
                continue;
            if (ShortcutClassificationHelper.IsUninstallShortcut(lnkPath, target))
                continue;
            if (ShortcutClassificationHelper.IsSystemExecutable(target))
                continue;
            if (ctx.ExistingApps.Contains((target, ctx.Credential.Sid)))
                continue;
            if (!File.Exists(target))
                continue;

            var isInProfileFolder = profilePrefix != null &&
                                    target.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase);

            var isCoveredByPublic = ctx.PublicTargets.Contains(target);
            var isCoveredByInteractiveUser = !ctx.Credential.IsInteractiveUser && ctx.InteractiveTargets.Contains(target);
            var isCovered = isCoveredByPublic || isCoveredByInteractiveUser;

            if (!isInProfileFolder && isCovered)
                continue;

            var key = (ctx.Credential.Sid, target);
            if (!ctx.Seen.Add(key))
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

            ctx.Results.Add(new StartMenuEntry(name, target, ctx.Credential.Sid, subfolder));
        }
    }

    private static string RerootToUserProfile(string target, bool isCurrentAccount, string currentUserProfile, string? profilePath)
    {
        if (isCurrentAccount || profilePath == null)
            return target;

        var targetProfile = profilePath.TrimEnd('\\', '/');
        if (!string.Equals(currentUserProfile, targetProfile, StringComparison.OrdinalIgnoreCase) &&
            target.StartsWith(currentUserProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return targetProfile + target[currentUserProfile.Length..];

        return target;
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
            : profilePathResolver.TryGetProfilePath(credential.Sid);

        var desktopPath = profilePathResolver.TryGetDesktopPath(credential.Sid, credential.IsCurrentAccount);
        CollectTargets(desktopPath, targets, currentUserProfile, profilePath);
        var programsPath = profilePathResolver.TryGetStartMenuProgramsPath(credential.Sid, credential.IsCurrentAccount);
        if (programsPath != null)
            CollectTargets(Path.GetDirectoryName(programsPath), targets, currentUserProfile, profilePath);
        return targets;
    }

    private void CollectTargets(string? dir, HashSet<string> targets,
        string? currentUserProfile = null, string? targetProfilePath = null)
    {
        if (dir == null || !Directory.Exists(dir))
            return;
        try
        {
            foreach (var lnkPath in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var target = shortcutHelper.GetShortcutTargetAndArgs(lnkPath).Target;
                    if (string.IsNullOrEmpty(target))
                        continue;
                    if (currentUserProfile != null)
                        target = RerootToUserProfile(target, isCurrentAccount: false, currentUserProfile, targetProfilePath);
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

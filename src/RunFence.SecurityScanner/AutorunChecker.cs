using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class AutorunChecker(IFileSystemDataAccess fileSystem, AclCheckHelper aclCheck)
{
    public void CheckAutorunExecutables(ScanContext ctx, Dictionary<string, string> userProfilePaths)
    {
        var adminSids = ctx.AdminSids;
        var interactiveUserSid = ctx.InteractiveUserSid;
        var autorun = ctx.Autorun;
        var autorunLocationPaths = ctx.AutorunLocationPaths;
        var insecureContainers = ctx.InsecureContainers;
        var findings = ctx.Findings;
        var seen = ctx.Seen;

        foreach (var rawExePath in autorun.Paths)
        {
            try
            {
                var exePath = Path.IsPathRooted(rawExePath)
                    ? rawExePath
                    : CommandLineParser.ResolveViaPath(rawExePath);
                if (exePath == null || !fileSystem.FileExists(exePath))
                    continue;

                if (IsInsideInsecureContainer(exePath, insecureContainers))
                    continue;

                var excluded = new HashSet<string>(adminSids, StringComparer.OrdinalIgnoreCase);
                if (autorun.PathExcluded.TryGetValue(rawExePath, out var pathExcluded))
                    excluded.UnionWith(pathExcluded);

                PerUserScanner.ApplyProfileExclusion(exePath, adminSids, interactiveUserSid, userProfilePaths, excluded);

                var category = autorun.PathCategories.GetValueOrDefault(rawExePath, StartupSecurityCategory.AutorunExecutable);
                var flaggedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var fileSecurity = fileSystem.GetFileSecurity(exePath);
                    var fileEffective = aclCheck.ComputeFilteredFileRights(fileSecurity, excluded);

                    var writeMask = SecurityScanner.TargetFileWriteRightsMask;
                    var individuallyReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (sid, rights) in fileEffective)
                    {
                        if ((rights & writeMask) != 0 && aclCheck.CachedGetGroupMemberSids(sid) == null)
                            individuallyReported.Add(sid);
                    }

                    foreach (var (sidStr, rights) in fileEffective)
                    {
                        var writeRights = rights & writeMask;
                        if (writeRights == 0)
                            continue;
                        if (aclCheck.IsRedundantGroupSid(sidStr, excluded, individuallyReported))
                            continue;

                        flaggedSids.Add(sidStr);
                        var principal = aclCheck.CachedResolveDisplayName(sidStr);
                        var key = (exePath, sidStr);
                        if (!seen.Add(key))
                            continue;
                        findings.Add(new StartupSecurityFinding(
                            category,
                            exePath, sidStr, principal, SecurityScanner.FormatFileSystemRights(writeRights, isDirectory: false), exePath));
                    }

                    if (!IsInsideAutorunLocation(exePath, autorunLocationPaths))
                    {
                        CheckFileReplaceable(exePath, fileEffective, excluded, flaggedSids, category, findings, seen);
                    }
                }
                catch (Exception ex)
                {
                    fileSystem.LogError($"Failed to read ACL for autorun executable '{exePath}': {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                fileSystem.LogError($"Failed to check autorun executable '{rawExePath}': {ex.Message}");
            }
        }
    }

    private void CheckFileReplaceable(string exePath,
        Dictionary<string, FileSystemRights> fileEffective, HashSet<string> excluded,
        HashSet<string> flaggedSids, StartupSecurityCategory category,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen)
    {
        var parentFolder = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(parentFolder))
            return;

        try
        {
            if (!fileSystem.DirectoryExists(parentFolder))
                return;

            var dirSecurity = fileSystem.GetDirectorySecurity(parentFolder);
            var folderEffective = aclCheck.ComputeFilteredFileRights(dirSecurity, excluded);

            foreach (var (sidStr, rights) in folderEffective)
            {
                if (flaggedSids.Contains(sidStr))
                    continue;

                bool hasDelete = fileEffective.TryGetValue(sidStr, out var fileRights)
                                 && (fileRights & FileSystemRights.Delete) != 0;
                bool hasWriteData = (rights & FileSystemRights.WriteData) != 0;

                if (hasDelete && hasWriteData)
                {
                    var principal = aclCheck.CachedResolveDisplayName(sidStr);
                    var key = (exePath, sidStr);
                    if (!seen.Add(key))
                        continue;
                    findings.Add(new StartupSecurityFinding(
                        category,
                        exePath, sidStr, principal,
                        "Replaceable (Delete + parent WriteData)", exePath));
                }
            }
        }
        catch (Exception ex)
        {
            fileSystem.LogError($"Failed to check file replaceability for '{exePath}': {ex.Message}");
        }
    }

    public void CollectStartupFolderExecutables(string? folderPath, AutorunContext autorun, HashSet<string>? ownerExcluded,
        StartupSecurityCategory category)
    {
        if (string.IsNullOrEmpty(folderPath))
            return;
        try
        {
            if (!fileSystem.DirectoryExists(folderPath))
                return;
            foreach (var filePath in fileSystem.GetFilesInFolder(folderPath))
            {
                if (string.IsNullOrEmpty(filePath))
                    continue;

                if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var target = fileSystem.ResolveShortcutTarget(filePath);
                        if (!string.IsNullOrEmpty(target))
                            SecurityScanner.AddAutorunPath(autorun, target, ownerExcluded, category);
                    }
                    catch (Exception ex)
                    {
                        fileSystem.LogError($"Failed to resolve shortcut target '{filePath}': {ex.Message}");
                    }
                }
                else if (!SecurityScanner.IsInertStartupFile(filePath))
                {
                    SecurityScanner.AddAutorunPath(autorun, filePath, ownerExcluded, category);
                }
            }
        }
        catch
        {
            /* already handled in folder check phase */
        }
    }

    public static bool IsInsideInsecureContainer(string path, HashSet<string> insecureContainers)
        => IsInsidePath(path, insecureContainers);

    private static bool IsInsideAutorunLocation(string path, HashSet<string> autorunLocationPaths)
        => IsInsidePath(path, autorunLocationPaths);

    private static bool IsInsidePath(string path, HashSet<string> paths)
    {
        foreach (var candidate in paths)
        {
            if (path.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) &&
                path.Length > candidate.Length &&
                (path[candidate.Length] == '\\' || path[candidate.Length] == '/'))
                return true;
        }

        return false;
    }
}
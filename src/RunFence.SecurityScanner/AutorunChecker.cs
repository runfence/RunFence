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

        EmitPendingWarnings(autorun.PendingWarnings, findings, seen);

        foreach (var rawExePath in autorun.Paths)
        {
            try
            {
                autorun.PathCommandContexts.TryGetValue(rawExePath, out var commandContexts);
                var exePath = ResolveAutorunPath(rawExePath, commandContexts);
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
                        var shortcut = fileSystem.ResolveShortcutTarget(filePath);
                        if (!string.IsNullOrWhiteSpace(shortcut?.TargetPath))
                        {
                            var context = new AutorunCommandContext(filePath, shortcut.Arguments, shortcut.WorkingDirectory, filePath);
                            var command = CommandLineParser.ResolveCommand(shortcut.TargetPath, shortcut.Arguments, shortcut.WorkingDirectory);
                            if (!string.IsNullOrWhiteSpace(command.ExecutablePath))
                                SecurityScanner.AddAutorunPath(autorun, command.ExecutablePath, ownerExcluded, category, context);
                            if (!string.IsNullOrWhiteSpace(command.WrapperPayloadPath))
                            {
                                SecurityScanner.AddAutorunPath(autorun, command.WrapperPayloadPath, ownerExcluded, category, context);
                            }
                            else if (command.WrapperPayloadParseFailed)
                            {
                                SecurityScanner.AddAutorunWarning(autorun, new AutorunWarning(
                                    category,
                                    filePath,
                                    "Startup shortcut",
                                    "Wrapper command could not be parsed to a payload executable.",
                                    filePath));
                            }
                        }
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

    private static string? ResolveAutorunPath(string rawPath, List<AutorunCommandContext>? commandContexts)
    {
        if (Path.IsPathRooted(rawPath))
            return rawPath;

        if (commandContexts != null)
        {
            foreach (var context in commandContexts)
            {
                var resolved = CommandLineParser.ResolvePath(rawPath, context.WorkingDirectory);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }
        }

        return CommandLineParser.ResolveViaPath(rawPath);
    }

    private static void EmitPendingWarnings(
        List<AutorunWarning> warnings,
        List<StartupSecurityFinding> findings,
        HashSet<(string, string)> seen)
    {
        foreach (var warning in warnings)
        {
            var key = (warning.TargetDescription, $"warning:{warning.Category}:{warning.Message}");
            if (!seen.Add(key))
                continue;

            findings.Add(new StartupSecurityFinding(
                warning.Category,
                warning.TargetDescription,
                "",
                warning.AffectedScope,
                warning.Message,
                warning.NavigationTarget));
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

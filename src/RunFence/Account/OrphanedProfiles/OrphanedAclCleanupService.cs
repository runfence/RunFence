using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Account.OrphanedProfiles;

/// <summary>
/// Scans filesystem paths (Public, ProgramData) for ACEs referencing deleted-account SIDs,
/// removes orphaned deny ACEs, and transfers orphaned allow ACEs to Administrators.
/// Extracted from OrphanedProfileService to separate profile lifecycle from ACL cleanup.
/// </summary>
public class OrphanedAclCleanupService(ILoggingService log) : IOrphanedAclCleanupService
{
    public async Task<List<(string Path, string Action, string? Error)>> CleanupAclReferencesAsync(
        List<string> sids, IProgress<AclCleanupProgress>? progress, CancellationToken ct)
    {
        if (sids.Count == 0)
            return [];

        var targetSids = new HashSet<SecurityIdentifier>();
        foreach (var sidStr in sids)
        {
            try
            {
                targetSids.Add(new SecurityIdentifier(sidStr));
            }
            catch
            {
                /* skip invalid SID strings */
            }
        }

        if (targetSids.Count == 0)
            return [];

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var report = new List<(string Path, string Action, string? Error)>();
        int objectsScanned = 0;
        int objectsFixed = 0;

        var scanPaths = new List<string>();
        var publicPath = Path.Combine(
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\",
            "Users", "Public");
        if (Directory.Exists(publicPath))
            scanPaths.Add(publicPath);

        var programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (Directory.Exists(programDataPath))
            scanPaths.Add(programDataPath);

        await Task.Run(() =>
        {
            // SeBackupPrivilege allows reading DACLs on protected objects.
            // SeRestorePrivilege allows writing DACLs on protected objects (bypasses WRITE_DAC check).
            // Both are needed for Helium cache files, Crypto keys, and similar OS-managed paths
            // that have the deleted user's SID in their ACL but block admin DACL writes.
            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            foreach (var rootPath in scanPaths)
            {
                ct.ThrowIfCancellationRequested();
                ScanDirectory(rootPath, targetSids, adminSid, report,
                    ref objectsScanned, ref objectsFixed, progress, ct);
            }
        }, ct);

        return report;
    }

    private void ScanDirectory(string path, HashSet<SecurityIdentifier> targetSids,
        SecurityIdentifier adminSid, List<(string, string, string?)> report,
        ref int objectsScanned, ref int objectsFixed,
        IProgress<AclCleanupProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Process the directory itself
        CleanupObjectAcl(path, isDirectory: true, targetSids, adminSid, report,
            ref objectsScanned, ref objectsFixed, progress);

        // Enumerate children — errors (access denied etc.) are silently skipped
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(path);
        }
        catch
        {
            return;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var attrs = File.GetAttributes(entry);

                // Don't follow reparse points (junctions/symlinks)
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attrs & FileAttributes.Directory) != 0)
                    ScanDirectory(entry, targetSids, adminSid, report,
                        ref objectsScanned, ref objectsFixed, progress, ct);
                else
                    CleanupObjectAcl(entry, isDirectory: false, targetSids, adminSid, report,
                        ref objectsScanned, ref objectsFixed, progress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                /* skip inaccessible entries */
            }
        }
    }

    private void CleanupObjectAcl(string path, bool isDirectory,
        HashSet<SecurityIdentifier> targetSids, SecurityIdentifier adminSid,
        List<(string, string, string?)> report,
        ref int objectsScanned, ref int objectsFixed,
        IProgress<AclCleanupProgress>? progress)
    {
        objectsScanned++;
        if (objectsScanned % 500 == 0)
            progress?.Report(new AclCleanupProgress(path, objectsFixed, objectsScanned));

        try
        {
            FileSystemSecurity security;
            if (isDirectory)
                security = new DirectoryInfo(path).GetAccessControl();
            else
                security = new FileInfo(path).GetAccessControl();

            // Include inherited rules so admin rights check considers inheritance
            var allRules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            var matchingRules = new List<FileSystemAccessRule>();

            foreach (FileSystemAccessRule rule in allRules)
            {
                // Only target explicit (non-inherited) orphaned ACEs for removal
                if (!rule.IsInherited &&
                    rule.IdentityReference is SecurityIdentifier ruleSid &&
                    targetSids.Contains(ruleSid))
                {
                    matchingRules.Add(rule);
                }
            }

            if (matchingRules.Count == 0)
                return;

            bool modified = false;
            var actions = new List<string>();

            foreach (var rule in matchingRules)
            {
                if (rule.AccessControlType == AccessControlType.Deny)
                {
                    // Deny ACE — always safe to remove
                    security.RemoveAccessRuleSpecific(rule);
                    actions.Add($"Removed deny ACE ({rule.FileSystemRights})");
                }
                else // Allow
                {
                    // Check if Administrators already has equivalent or greater rights
                    // (allRules includes inherited, so inherited FullControl is accounted for)
                    var adminHasRights = HasEquivalentAdminRights(allRules, adminSid, rule.FileSystemRights);

                    if (!adminHasRights)
                    {
                        // Add equivalent Allow for Administrators before removing orphaned ACE
                        var adminRule = new FileSystemAccessRule(
                            adminSid, rule.FileSystemRights,
                            rule.InheritanceFlags, rule.PropagationFlags,
                            AccessControlType.Allow);
                        security.AddAccessRule(adminRule);
                        actions.Add($"Transferred allow ACE to Administrators ({rule.FileSystemRights})");
                    }
                    else
                    {
                        actions.Add($"Removed allow ACE ({rule.FileSystemRights})");
                    }

                    security.RemoveAccessRuleSpecific(rule);
                }

                modified = true;
            }

            if (modified)
            {
                if (isDirectory)
                    new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
                else
                    new FileInfo(path).SetAccessControl((FileSecurity)security);

                objectsFixed++;
                report.Add((path, string.Join("; ", actions), null));
            }
        }
        catch (Exception ex)
        {
            report.Add((path, "Failed", ex.Message));
            log.Warn($"ACL cleanup failed for '{path}': {ex.Message}");
        }
    }

    private static bool HasEquivalentAdminRights(
        AuthorizationRuleCollection rules, SecurityIdentifier adminSid, FileSystemRights requiredRights)
    {
        FileSystemRights adminAllow = 0;
        FileSystemRights adminDeny = 0;

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is not SecurityIdentifier sid || sid != adminSid)
                continue;

            if (rule.AccessControlType == AccessControlType.Allow)
                adminAllow |= rule.FileSystemRights;
            else
                adminDeny |= rule.FileSystemRights;
        }

        var effective = adminAllow & ~adminDeny;
        return (effective & requiredRights) == requiredRights;
    }
}
using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class AclCheckHelper(IEnvironmentDataAccess environment, IFileSystemDataAccess fileSystem, IAutorunRegistryAccess registry)
{
    private readonly Dictionary<string, string> _displayNameCache = new(StringComparer.OrdinalIgnoreCase);
    // Pre-populated in bulk by BulkPrewarmGroupMemberSids before each filtering pass.
    // Falls back to TryGetGroupMemberSids via Lazy factory for any uncached SIDs.
    private readonly ConcurrentDictionary<string, Lazy<HashSet<string>?>> _groupMemberCache =
        new(StringComparer.OrdinalIgnoreCase);

    public bool CheckContainerAcl(FileSystemSecurity security, string targetPath,
        HashSet<string> excludedSids, StartupSecurityCategory category,
        FileSystemRights writeMask, List<StartupSecurityFinding> findings,
        HashSet<(string, string)> seen, string? navigationTarget = null)
    {
        CheckFileSystemOwner(security, targetPath, excludedSids, category, findings, seen, navigationTarget ?? targetPath);
        var effective = ComputeFilteredFileRights(security, excludedSids, writeMask);
        bool anyFlagged = false;
        foreach (var (sidStr, rights) in effective)
        {
            var writeRights = rights & writeMask;
            if (writeRights == 0)
                continue;

            var principal = CachedResolveDisplayName(sidStr);
            var key = (targetPath, sidStr);
            if (!seen.Add(key))
                continue;

            findings.Add(new StartupSecurityFinding(category, targetPath, sidStr, principal,
                SecurityScanner.FormatFileSystemRights(writeRights, isDirectory: true), navigationTarget ?? targetPath));
            anyFlagged = true;
        }

        return anyFlagged;
    }

    public void CheckFileInsideLocationAcl(FileSystemSecurity security, string targetPath,
        HashSet<string> excludedSids, StartupSecurityCategory category,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen)
    {
        var effective = ComputeFilteredFileRights(security, excludedSids, SecurityScanner.TargetFileWriteRightsMask);
        foreach (var (sidStr, rights) in effective)
        {
            var writeRights = rights & SecurityScanner.TargetFileWriteRightsMask;
            if (writeRights == 0)
                continue;

            var principal = CachedResolveDisplayName(sidStr);
            var key = (targetPath, sidStr);
            if (!seen.Add(key))
                continue;

            findings.Add(new StartupSecurityFinding(category, targetPath, sidStr, principal,
                SecurityScanner.FormatFileSystemRights(writeRights, isDirectory: false), targetPath));
        }
    }

    public void CheckRegistryKeyAcl(RegistrySecurity security, string displayPath,
        HashSet<string> excludedSids, RegistryRights writeMask, StartupSecurityCategory category,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen,
        string? navigationTarget = null)
    {
        CheckRegistryOwner(security, displayPath, excludedSids, category, findings, seen, navigationTarget);
        var effective = ComputeFilteredRegistryRights(security, excludedSids, writeMask);
        foreach (var (sidStr, rights) in effective)
        {
            var writeRights = rights & writeMask;
            if (writeRights == 0)
                continue;

            var principal = CachedResolveDisplayName(sidStr);
            var key = (displayPath, sidStr);
            if (!seen.Add(key))
                continue;

            findings.Add(new StartupSecurityFinding(category, displayPath, sidStr, principal,
                writeRights.ToString(), navigationTarget));
        }
    }

    public void CheckRegistryKey(RegistryKey hive, string subKeyPath, string displayPath,
        HashSet<string> excludedSids, RegistryRights writeMask, StartupSecurityCategory category,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen,
        AutorunContext autorun, HashSet<string>? ownerExcluded, string? navigationTarget = null)
    {
        try
        {
            var security = registry.GetRegistryKeySecurity(hive, subKeyPath);
            if (security == null)
                return;

            CheckRegistryKeyAcl(security, displayPath, excludedSids, writeMask, category, findings, seen, navigationTarget);

            foreach (var exePath in registry.GetRegistryAutorunPaths(hive, subKeyPath))
            {
                if (!string.IsNullOrEmpty(exePath))
                    SecurityScanner.AddAutorunPath(autorun, exePath, ownerExcluded, category);
            }
        }
        catch (Exception ex)
        {
            fileSystem.LogError($"Failed to check registry key '{displayPath}': {ex.Message}");
        }
    }

    public Dictionary<string, FileSystemRights> ComputeFilteredFileRights(
        FileSystemSecurity security, HashSet<string> excludedSids,
        FileSystemRights writeMask = 0)
    {
        var effective = AclComputeHelper.ComputeEffectiveFileRights(security, excludedSids);
        FilterRedundantGroupSids(effective, excludedSids, sid => (effective[sid] & writeMask) != 0);
        return effective;
    }

    private Dictionary<string, RegistryRights> ComputeFilteredRegistryRights(
        RegistrySecurity security, HashSet<string> excludedSids,
        RegistryRights writeMask = 0)
    {
        var effective = AclComputeHelper.ComputeEffectiveRegistryRights(security, excludedSids);
        FilterRedundantGroupSids(effective, excludedSids, sid => (effective[sid] & writeMask) != 0);
        return effective;
    }

    /// <summary>
    /// Removes SIDs from <paramref name="effective"/> that are redundant group SIDs —
    /// i.e., groups whose non-excluded, non-system members are all individually reported.
    /// </summary>
    private void FilterRedundantGroupSids<TRights>(
        Dictionary<string, TRights> effective,
        HashSet<string> excludedSids,
        Func<string, bool> hasWriteRights)
    {
        // Resolve all SIDs for this ACL in one LsaLookupSids call, caching results.
        // Subsequent CachedGetGroupMemberSids calls below are all O(1) cache hits.
        BulkPrewarmGroupMemberSids(effective.Keys);

        HashSet<string>? individuallyReported = null;
        foreach (var sid in effective.Keys)
        {
            if (hasWriteRights(sid) && CachedGetGroupMemberSids(sid) == null)
            {
                individuallyReported ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                individuallyReported.Add(sid);
            }
        }

        var toRemove = effective.Keys.Where(sid => IsRedundantGroupSid(sid, excludedSids, individuallyReported)).ToList();

        foreach (var sid in toRemove)
            effective.Remove(sid);
    }

    public string CachedResolveDisplayName(string sidString)
    {
        if (_displayNameCache.TryGetValue(sidString, out var cached))
            return cached;

        var resolved = environment.ResolveDisplayName(sidString);
        if (string.Equals(resolved, sidString, StringComparison.OrdinalIgnoreCase))
            resolved = sidString + " (unknown SID)";

        _displayNameCache[sidString] = resolved;
        return resolved;
    }

    public HashSet<string>? CachedGetGroupMemberSids(string sid)
    {
        return _groupMemberCache.GetOrAdd(sid,
            key => new Lazy<HashSet<string>?>(() => environment.TryGetGroupMemberSids(key))).Value;
    }

    /// <summary>
    /// Pre-warms the group member cache for the given SIDs using a single bulk
    /// <c>LsaLookupSids</c> call. Subsequent <see cref="CachedGetGroupMemberSids"/> calls
    /// for these SIDs return immediately from the cache.
    /// </summary>
    public void StartPrewarmGroupMembers(params string[] groupSids) =>
        BulkPrewarmGroupMemberSids(groupSids);

    /// <summary>
    /// Resolves all uncached SIDs via <see cref="IEnvironmentDataAccess.BulkLookupGroupMemberSids"/>
    /// and stores the results in <see cref="_groupMemberCache"/> as completed <see cref="Lazy{T}"/>
    /// instances so that <see cref="CachedGetGroupMemberSids"/> returns immediately for all of them.
    /// </summary>
    private void BulkPrewarmGroupMemberSids(IEnumerable<string> sids)
    {
        var uncached = sids.Where(sid => !_groupMemberCache.ContainsKey(sid)).ToList();
        if (uncached.Count == 0)
            return;

        var results = environment.BulkLookupGroupMemberSids(uncached);
        foreach (var (sid, members) in results)
            _groupMemberCache.TryAdd(sid, new Lazy<HashSet<string>?>(members));
    }

    public bool IsRedundantGroupSid(string sid, HashSet<string> excludedSids,
        HashSet<string>? alsoReportedSids = null)
    {
        var members = CachedGetGroupMemberSids(sid);
        if (members == null)
            return false;

        // Empty member list means enumeration failed or the group is empty — do not suppress.
        // A group that enumerates as empty may just be unenumerable; safer to report it.
        if (members.Count == 0)
            return false;

        foreach (var memberSid in members)
        {
            if (excludedSids.Contains(memberSid))
                continue;
            try
            {
                if (AclComputeHelper.IsTrustedSystemSid(new SecurityIdentifier(memberSid)))
                    continue;
            }
            catch
            {
                /* invalid SID format — treat as non-excluded */
            }

            if (alsoReportedSids != null && alsoReportedSids.Contains(memberSid))
                continue;
            return false;
        }

        return true;
    }

    private void CheckFileSystemOwner(FileSystemSecurity security, string targetPath,
        HashSet<string> excludedSids, StartupSecurityCategory category,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen, string? navigationTarget)
    {
        try
        {
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner)
                return;
            if (AclComputeHelper.IsTrustedSystemSid(owner))
                return;
            if (excludedSids.Contains(owner.Value))
                return;
            // Dedup: the same (path, SID) owner finding can be reported multiple times when
            // multiple scan passes visit the same path (e.g., container + file-inside-location).
            if (!seen.Add((targetPath, owner.Value + ":owner")))
                return;
            var principal = CachedResolveDisplayName(owner.Value);
            findings.Add(new StartupSecurityFinding(category, targetPath, owner.Value, principal,
                "Owner", navigationTarget ?? targetPath));
        }
        catch
        {
            /* best effort */
        }
    }

    private void CheckRegistryOwner(RegistrySecurity security, string displayPath,
        HashSet<string> excludedSids, StartupSecurityCategory category,
        List<StartupSecurityFinding> findings, HashSet<(string, string)> seen, string? navigationTarget)
    {
        try
        {
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner)
                return;
            if (AclComputeHelper.IsTrustedSystemSid(owner))
                return;
            if (excludedSids.Contains(owner.Value))
                return;
            // Dedup: same as CheckFileSystemOwner — prevents duplicate owner findings when the
            // same registry key is visited by multiple scan passes.
            if (!seen.Add((displayPath, owner.Value + ":owner")))
                return;
            var principal = CachedResolveDisplayName(owner.Value);
            findings.Add(new StartupSecurityFinding(category, displayPath, owner.Value, principal,
                "Owner", navigationTarget));
        }
        catch
        {
            /* best effort */
        }
    }
}
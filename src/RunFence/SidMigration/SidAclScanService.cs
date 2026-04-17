using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SidMigration;

/// <summary>
/// Handles filesystem ACL scanning for SID migration: discovers orphaned SIDs on disk,
/// scans for files/directories with ACEs referencing given SIDs, and applies SID replacements.
/// Extracted from <see cref="SidMigrationService"/> to separate the ACL traversal concern.
/// </summary>
public class SidAclScanService(ILoggingService log, ISidResolver sidResolver, IFileSystemAclTraverser traverser) : ISidAclScanService
{
    public async Task<List<OrphanedSid>> DiscoverOrphanedSidsAsync(
        IReadOnlyList<string> rootPaths,
        IProgress<(long scanned, long sidsFound)> onProgress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var sidCounts = new Dictionary<string, OrphanedSid>(StringComparer.OrdinalIgnoreCase);

            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            foreach (var (path, _, security) in traverser.Traverse(rootPaths, new Progress<long>(scanned => onProgress.Report((scanned, sidCounts.Count))), ct))
            {
                // Collect SIDs from explicit ACEs
                var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    if (rule.IdentityReference is SecurityIdentifier sid)
                    {
                        var sidStr = sid.Value;
                        if (!sidStr.StartsWith("S-1-5-21-", StringComparison.Ordinal))
                            continue;

                        if (!sidCounts.TryGetValue(sidStr, out var entry))
                        {
                            entry = new OrphanedSid { Sid = sidStr };
                            sidCounts[sidStr] = entry;
                        }

                        if (entry.SamplePaths.Count < OrphanedSid.MaxSamplePaths &&
                            (entry.SamplePaths.Count == 0 || entry.SamplePaths[^1] != path))
                            entry.SamplePaths.Add(path);
                        entry.AceCount++;
                    }
                }

                // Collect owner SID
                try
                {
                    var owner = security.GetOwner(typeof(SecurityIdentifier));
                    if (owner is SecurityIdentifier ownerSid)
                    {
                        var ownerStr = ownerSid.Value;
                        if (ownerStr.StartsWith("S-1-5-21-", StringComparison.Ordinal))
                        {
                            if (!sidCounts.TryGetValue(ownerStr, out var entry))
                            {
                                entry = new OrphanedSid { Sid = ownerStr };
                                sidCounts[ownerStr] = entry;
                            }

                            if (entry.SamplePaths.Count < OrphanedSid.MaxSamplePaths &&
                                (entry.SamplePaths.Count == 0 || entry.SamplePaths[^1] != path))
                                entry.SamplePaths.Add(path);
                            entry.OwnerCount++;
                        }
                    }
                }
                catch
                {
                } // skip unreadable ACL entries
            }

            // Resolve each unique SID — filter to orphaned only
            var failedDomainPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orphanedList = new List<OrphanedSid>();

            foreach (var (sidStr, orphanedSid) in sidCounts)
            {
                ct.ThrowIfCancellationRequested();

                // Extract domain prefix (S-1-5-21-XXXXX-XXXXX-XXXXX)
                var parts = sidStr.Split('-');
                var domainPrefix = parts.Length >= 7
                    ? string.Join("-", parts.Take(7))
                    : sidStr;

                bool isOrphaned;
                if (failedDomainPrefixes.Contains(domainPrefix))
                {
                    isOrphaned = true;
                }
                else
                {
                    // Note: resolveCts timeout (3s) passed to Task.Run is ineffective because TryResolveName
                    // does not accept a CancellationToken — it ignores cancellation internally.
                    // The actual timeout is enforced by Wait(3000, ct) below.
                    // Consequence: if TryResolveName blocks (e.g. unreachable DC), the background task continues
                    // running after Wait returns false — orphaned tasks may accumulate for unreachable domains.
                    // The failedDomainPrefixes cache prevents repeated waits for the same domain prefix.
                    using var resolveCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var resolveTask = Task.Run(() => sidResolver.TryResolveName(sidStr), resolveCts.Token);
                    var completed = resolveTask.Wait(3000, ct);
                    if (!completed || !resolveTask.IsCompletedSuccessfully || resolveTask.Result == null)
                    {
                        isOrphaned = true;
                        if (!completed)
                            failedDomainPrefixes.Add(domainPrefix);
                    }
                    else
                    {
                        isOrphaned = false;
                    }
                }

                if (isOrphaned)
                    orphanedList.Add(orphanedSid);
            }

            return orphanedList;
        }, ct);
    }

    // Note: DiscoverOrphanedSidsAsync and ScanAsync share a similar structural pattern
    // (traverse ACLs, accumulate per-SID counts/matches, report progress). Extraction into
    // a shared helper is not practical because the accumulators differ in type and semantics:
    // DiscoverOrphanedSids builds OrphanedSid objects with sample paths + resolution; ScanAsync
    // builds SidMigrationMatch objects with per-SID ACE counts and owner SIDs.
    public async Task<List<SidMigrationMatch>> ScanAsync(
        IReadOnlyList<string> rootPaths,
        IReadOnlyList<SidMigrationMapping> mappings,
        IProgress<(long scanned, long found)> onProgress,
        CancellationToken ct)
    {
        var oldSids = new HashSet<string>(mappings.Select(m => m.OldSid), StringComparer.OrdinalIgnoreCase);
        var oldSidObjects = new HashSet<SecurityIdentifier>();
        foreach (var s in oldSids)
        {
            try
            {
                oldSidObjects.Add(new SecurityIdentifier(s));
            }
            catch
            {
            }
        }

        return await Task.Run(() =>
        {
            var matches = new List<SidMigrationMatch>();
            long foundCount = 0;

            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            foreach (var (path, isDirectory, security) in traverser.Traverse(rootPaths, new Progress<long>(scanned => onProgress.Report((scanned, foundCount))), ct))
            {
                var aceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                string? ownerOldSid = null;
                var matchType = SidMigrationMatchType.None;

                var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    if (rule.IdentityReference is SecurityIdentifier sid && oldSidObjects.Contains(sid))
                    {
                        matchType |= SidMigrationMatchType.Ace;
                        aceCounts.TryGetValue(sid.Value, out var count);
                        aceCounts[sid.Value] = count + 1;
                    }
                }

                try
                {
                    var owner = security.GetOwner(typeof(SecurityIdentifier));
                    if (owner is SecurityIdentifier ownerSid && oldSids.Contains(ownerSid.Value))
                    {
                        matchType |= SidMigrationMatchType.Owner;
                        ownerOldSid = ownerSid.Value;
                    }
                }
                catch
                {
                }

                if (matchType != SidMigrationMatchType.None)
                {
                    matches.Add(new SidMigrationMatch
                    {
                        Path = path,
                        IsDirectory = isDirectory,
                        MatchType = matchType,
                        AceCountByOldSid = aceCounts,
                        OwnerOldSid = ownerOldSid
                    });
                    foundCount++;
                }
            }

            return matches;
        }, ct);
    }

    public async Task<(long applied, long errors)> ApplyAsync(
        IReadOnlyList<SidMigrationMatch> hits,
        IReadOnlyList<SidMigrationMapping> mappings,
        IProgress<MigrationProgress> onProgress,
        CancellationToken ct)
    {
        var sidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
            sidMap[m.OldSid] = m.NewSid;

        return await Task.Run(() =>
        {
            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            long applied = 0;
            long errors = 0;

            foreach (var hit in hits)
            {
                ct.ThrowIfCancellationRequested();
                onProgress.Report(new MigrationProgress(applied, hits.Count, hit.Path));

                try
                {
                    FileSystemSecurity security;
                    bool isDir = hit.IsDirectory;

                    if (isDir)
                    {
                        var dirInfo = new DirectoryInfo(hit.Path);
                        security = dirInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
                    }
                    else
                    {
                        var fileInfo = new FileInfo(hit.Path);
                        security = fileInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
                    }

                    // Replace ACEs — collect matching rules first, then remove+add
                    // in a separate pass to avoid modifying the collection during enumeration.
                    if (hit.MatchType.HasFlag(SidMigrationMatchType.Ace))
                    {
                        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                        var replacements = new List<(FileSystemAccessRule old, FileSystemAccessRule replacement)>();
                        foreach (FileSystemAccessRule rule in rules)
                        {
                            if (rule.IdentityReference is SecurityIdentifier oldSid &&
                                sidMap.TryGetValue(oldSid.Value, out var newSidStr))
                            {
                                var newSid = new SecurityIdentifier(newSidStr);
                                replacements.Add((rule, new FileSystemAccessRule(
                                    newSid, rule.FileSystemRights,
                                    rule.InheritanceFlags, rule.PropagationFlags,
                                    rule.AccessControlType)));
                            }
                        }

                        foreach (var (old, replacement) in replacements)
                        {
                            security.RemoveAccessRuleSpecific(old);
                            security.AddAccessRule(replacement);
                        }
                    }

                    // Replace owner
                    if (hit.MatchType.HasFlag(SidMigrationMatchType.Owner) && hit.OwnerOldSid != null &&
                        sidMap.TryGetValue(hit.OwnerOldSid, out var newOwnerStr))
                    {
                        security.SetOwner(new SecurityIdentifier(newOwnerStr));
                    }

                    // Write back
                    if (isDir)
                        new DirectoryInfo(hit.Path).SetAccessControl((DirectorySecurity)security);
                    else
                        new FileInfo(hit.Path).SetAccessControl((FileSecurity)security);

                    applied++;
                }
                catch (Exception ex)
                {
                    log.Error($"SID migration failed for {hit.Path}", ex);
                    errors++;
                }
            }

            return (applied, errors);
        }, ct);
    }
}
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
public class SidAclScanService(
    ILoggingService log,
    ISidResolver sidResolver,
    IFileSystemAclTraverser traverser,
    IAclAccessor aclAccessor) : ISidAclScanService
{
    public async Task<List<OrphanedSid>> DiscoverOrphanedSidsAsync(
        IReadOnlyList<string> rootPaths,
        IProgress<(long scanned, long sidsFound)> onProgress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ValidateRootAccessibility(rootPaths);
            var sidCounts = new Dictionary<string, OrphanedSid>(StringComparer.OrdinalIgnoreCase);

            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            TraverseWithSecurityInfo(rootPaths, new Progress<long>(scanned => onProgress.Report((scanned, sidCounts.Count))), ct,
                (path, _, security) =>
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
                });

            // Resolve each unique SID — filter to orphaned only
            var unresolvedDomainPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orphanedList = new List<OrphanedSid>();
            int unresolvedTaskCount = 0;

            foreach (var (sidStr, orphanedSid) in sidCounts)
            {
                ct.ThrowIfCancellationRequested();

                // Extract domain prefix (S-1-5-21-XXXXX-XXXXX-XXXXX)
                var parts = sidStr.Split('-');
                var domainPrefix = parts.Length >= 7
                    ? string.Join("-", parts.Take(7))
                    : sidStr;

                OrphanedSidClassification? classification = null;
                if (unresolvedDomainPrefixes.Contains(domainPrefix))
                {
                    classification = OrphanedSidClassification.Unresolved;
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
                    bool completed;
                    try
                    {
                        completed = resolveTask.Wait(3000, ct);
                    }
                    catch (AggregateException)
                    {
                        completed = true;
                    }

                    if (!completed)
                    {
                        log.Warn($"SidAclScanService: SID resolution timed out for {sidStr} (domain prefix {domainPrefix}); marking as unresolved.");
                        unresolvedDomainPrefixes.Add(domainPrefix);
                        unresolvedTaskCount++;
                        classification = OrphanedSidClassification.Unresolved;
                    }
                    else if (resolveTask.IsFaulted || resolveTask.IsCanceled)
                    {
                        log.Warn($"SidAclScanService: SID resolution failed for {sidStr} (domain prefix {domainPrefix}); marking as unresolved.");
                        unresolvedDomainPrefixes.Add(domainPrefix);
                        classification = OrphanedSidClassification.Unresolved;
                    }
                    else if (resolveTask.Result == null)
                    {
                        classification = OrphanedSidClassification.ConfirmedOrphaned;
                    }
                    else
                    {
                        classification = null;
                    }
                }

                if (classification != null)
                {
                    orphanedSid.Classification = classification.Value;
                    orphanedList.Add(orphanedSid);
                }
            }

            if (unresolvedTaskCount > 0)
                log.Warn($"SidAclScanService: {unresolvedTaskCount} SID resolution task(s) timed out and were left running in the background. Affected domain prefix(es): {string.Join(", ", unresolvedDomainPrefixes)}");

            return orphanedList;
        }, ct);
    }

    public async Task<List<SidMigrationMatch>> ScanAsync(
        IReadOnlyList<string> rootPaths,
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        IProgress<(long scanned, long found)> onProgress,
        CancellationToken ct)
    {
        var migrateSids = new HashSet<string>(mappings.Select(m => m.OldSid), StringComparer.OrdinalIgnoreCase);
        var ownerSids = new HashSet<string>(migrateSids, StringComparer.OrdinalIgnoreCase);
        ownerSids.UnionWith(sidsToDelete);
        var aceSids = new HashSet<string>(migrateSids, StringComparer.OrdinalIgnoreCase);
        aceSids.UnionWith(sidsToDelete);
        var aceSidObjects = new HashSet<SecurityIdentifier>();
        foreach (var s in aceSids)
        {
            try
            {
                aceSidObjects.Add(new SecurityIdentifier(s));
            }
            catch
            {
            }
        }

        return await Task.Run(() =>
        {
            ValidateRootAccessibility(rootPaths);
            var matches = new List<SidMigrationMatch>();
            long foundCount = 0;

            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            TraverseWithSecurityInfo(rootPaths, new Progress<long>(scanned => onProgress.Report((scanned, foundCount))), ct,
                (path, isDirectory, security) =>
                {
                    var aceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    string? ownerOldSid = null;
                    var matchType = SidMigrationMatchType.None;

                    var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                    foreach (FileSystemAccessRule rule in rules)
                    {
                        if (rule.IdentityReference is SecurityIdentifier sid && aceSidObjects.Contains(sid))
                        {
                            matchType |= SidMigrationMatchType.Ace;
                            aceCounts.TryGetValue(sid.Value, out var count);
                            aceCounts[sid.Value] = count + 1;
                        }
                    }

                    try
                    {
                        var owner = security.GetOwner(typeof(SecurityIdentifier));
                        if (owner is SecurityIdentifier ownerSid && ownerSids.Contains(ownerSid.Value))
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
                });

            return matches;
        }, ct);
    }

    public async Task<(long applied, long errors)> ApplyAsync(
        IReadOnlyList<SidMigrationMatch> hits,
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        IProgress<MigrationProgress> onProgress,
        CancellationToken ct)
    {
        var sidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
            sidMap[m.OldSid] = m.NewSid;
        var deleteSids = new HashSet<string>(sidsToDelete, StringComparer.OrdinalIgnoreCase);

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
                    aclAccessor.ModifyOwnerAndAclWithFallback(hit.Path, hit.IsDirectory, security =>
                    {
                        bool changed = false;

                        // Replace ACEs — collect matching rules first, then remove+add
                        // in a separate pass to avoid modifying the collection during enumeration.
                        if (hit.MatchType.HasFlag(SidMigrationMatchType.Ace))
                        {
                            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                            var replacements = new List<(FileSystemAccessRule old, FileSystemAccessRule replacement)>();
                            var removals = new List<FileSystemAccessRule>();
                            foreach (FileSystemAccessRule rule in rules)
                            {
                                if (rule.IdentityReference is not SecurityIdentifier oldSid)
                                    continue;

                                if (sidMap.TryGetValue(oldSid.Value, out var newSidStr))
                                {
                                    var newSid = new SecurityIdentifier(newSidStr);
                                    replacements.Add((rule, new FileSystemAccessRule(
                                        newSid, rule.FileSystemRights,
                                        rule.InheritanceFlags, rule.PropagationFlags,
                                        rule.AccessControlType)));
                                }
                                else if (deleteSids.Contains(oldSid.Value))
                                {
                                    removals.Add(rule);
                                }
                            }

                            if (removals.Count > 0 || replacements.Count > 0)
                                changed = true;

                            foreach (var removal in removals)
                                security.RemoveAccessRuleSpecific(removal);

                            foreach (var (old, replacement) in replacements)
                            {
                                security.RemoveAccessRuleSpecific(old);
                                security.AddAccessRule(replacement);
                            }
                        }

                        if (hit.MatchType.HasFlag(SidMigrationMatchType.Owner) &&
                            hit.OwnerOldSid != null &&
                            sidMap.TryGetValue(hit.OwnerOldSid, out var newOwnerStr))
                        {
                            security.SetOwner(new SecurityIdentifier(newOwnerStr));
                            changed = true;
                        }

                        return changed;
                    });

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

    private void TraverseWithSecurityInfo(
        IReadOnlyList<string> rootPaths,
        IProgress<long> progress,
        CancellationToken ct,
        Action<string, bool, FileSystemSecurity> process)
    {
        foreach (var (path, isFolder, security) in traverser.Traverse(rootPaths, progress, ct))
            process(path, isFolder, security);
    }

    private void ValidateRootAccessibility(IReadOnlyList<string> rootPaths)
    {
        foreach (var root in rootPaths)
        {
            if (!aclAccessor.PathExists(root, out bool isFolder) || !isFolder)
                continue;
            try
            {
                _ = aclAccessor.GetSecurity(root);
            }
            catch (Exception ex)
            {
                throw new IOException($"Protected root ACL could not be read: '{root}'.", ex);
            }
        }
    }
}

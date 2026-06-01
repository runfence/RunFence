using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl;

public sealed class GrantRuntimeSnapshotService(
    UiThreadDatabaseAccessor dbAccessor,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver)
{
    public GrantRuntimeEntrySnapshot CaptureGrantSnapshot(string sid, string path, bool isDeny)
    {
        var normalizedPath = Path.GetFullPath(path);
        var entry = dbAccessor.Read(db =>
            GrantEntryLookup.FindGrantEntryInDb(db, sid, normalizedPath, isDeny)?.Clone());
        return new GrantRuntimeEntrySnapshot(sid, normalizedPath, isTraverseOnly: false, isDeny, entry);
    }

    public GrantRuntimeEntrySnapshot CaptureTraverseSnapshot(string sid, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var entry = dbAccessor.Read(db =>
            FindTraverseEntry(db, sid, normalizedPath, includeManualSharedEntries: true)?.Clone());
        return new GrantRuntimeEntrySnapshot(sid, normalizedPath, isTraverseOnly: true, isDeny: false, entry);
    }

    public IReadOnlyList<GrantRuntimeEntrySnapshot> CaptureEntrySnapshotsForPath(
        string path,
        bool isTraverseOnly)
    {
        var normalizedPath = Path.GetFullPath(path);
        return dbAccessor.Read(db =>
            db.Accounts
                .Where(account => !AclHelper.IsContainerSid(account.Sid) && !AclHelper.IsLowIntegritySid(account.Sid))
                .Select(account => new
                {
                    account.Sid,
                    Entry = account.Grants.FirstOrDefault(entry =>
                        entry.IsTraverseOnly == isTraverseOnly &&
                        !entry.IsDeny &&
                        string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                })
                .Where(match => match.Entry != null)
                .Select(match => new GrantRuntimeEntrySnapshot(
                    match.Sid,
                    normalizedPath,
                    isTraverseOnly,
                    isDeny: false,
                    match.Entry!.Clone()))
                .ToList());
    }

    public void RestoreGrantSnapshot(GrantRuntimeEntrySnapshot snapshot)
    {
        dbAccessor.Write(db =>
        {
            var grants = snapshot.Entry != null
                ? db.GetOrCreateAccount(snapshot.Sid).Grants
                : db.GetAccount(snapshot.Sid)?.Grants;
            if (grants == null)
                return;

            var current = GrantEntryLookup.FindGrantEntryInList(grants, snapshot.Path, snapshot.IsDeny);
            if (current != null)
                grants.Remove(current);

            if (snapshot.Entry != null)
                grants.Add(snapshot.Entry.Clone());

            db.RemoveAccountIfEmpty(snapshot.Sid);
        });
    }

    public void RestoreTraverseSnapshot(GrantRuntimeEntrySnapshot snapshot)
    {
        dbAccessor.Write(db =>
        {
            traverseGrantOwnerResolver.RestoreTraverseEntry(db, snapshot.Sid, snapshot.Path, snapshot.Entry);

            if (!AclHelper.IsSpecificContainerSid(snapshot.Sid))
                db.RemoveAccountIfEmpty(snapshot.Sid);
        });
    }

    public GrantedPathEntry? FindTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries)
        => traverseGrantOwnerResolver.FindTraverseEntry(
            database,
            sid,
            normalizedPath,
            includeManualSharedEntries);

    public void RestoreLinkedEntrySnapshots(
        string path,
        bool isTraverseOnly,
        string sourceSid,
        IReadOnlyList<GrantRuntimeEntrySnapshot> snapshots)
    {
        var normalizedPath = Path.GetFullPath(path);
        var snapshotBySid = snapshots.ToDictionary(snapshot => snapshot.Sid, StringComparer.OrdinalIgnoreCase);

        dbAccessor.Write(db =>
        {
            var candidateSids = db.Accounts
                .Where(account => !AclHelper.IsContainerSid(account.Sid) && !AclHelper.IsLowIntegritySid(account.Sid))
                .Where(account => account.Grants.Any(entry =>
                    entry.IsTraverseOnly == isTraverseOnly &&
                    !entry.IsDeny &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                .Select(account => account.Sid)
                .Concat(snapshotBySid.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidateSid in candidateSids)
            {
                var account = db.GetAccount(candidateSid);
                var grants = account?.Grants;
                snapshotBySid.TryGetValue(candidateSid, out var snapshot);
                var snapshotEntry = snapshot?.Entry;
                var hasSnapshot = snapshotEntry != null;
                var current = grants?.FirstOrDefault(entry =>
                    entry.IsTraverseOnly == isTraverseOnly &&
                    !entry.IsDeny &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
                var currentLinkedToSource = current != null && IsEntryLinkedToSource(current, sourceSid);
                if (current != null && (hasSnapshot || currentLinkedToSource))
                    grants!.Remove(current);

                if (!hasSnapshot)
                {
                    if (account != null)
                        db.RemoveAccountIfEmpty(candidateSid);
                    continue;
                }

                db.GetOrCreateAccount(candidateSid).Grants.Add(snapshotEntry!.Clone());
            }
        });
    }

    private static bool IsEntryLinkedToSource(GrantedPathEntry entry, string sourceSid)
        => entry.SourceSids?.Contains(sourceSid, StringComparer.OrdinalIgnoreCase) == true ||
           string.Equals(entry.OwnerContainerSid, sourceSid, StringComparison.OrdinalIgnoreCase);
}

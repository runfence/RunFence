using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Synchronizes Low Integrity (S-1-16-4096) ACL grants when source account grants are removed.
/// When a non-Low-IL account grant is removed, that account SID is removed from the Low IL
/// grant's <c>SourceSids</c>. When all source SIDs are gone, the Low IL grant is removed;
/// Low IL grant ACE and SACL restore work only runs when the caller requested filesystem updates.
/// Low IL grants with <c>SourceSids == null</c> (manually added via ACL Manager) are never
/// auto-cleaned.
/// </summary>
public sealed class LowIntegrityGrantSync(
    IGrantCoreOperations grantCore,
    ITraverseCoreOperations traverseCore,
    IMandatoryLabelService mandatoryLabelService,
    UiThreadDatabaseAccessor dbAccessor)
    : GrantSyncBase(grantCore, traverseCore, dbAccessor)
{
    /// <summary>
    /// Removes <paramref name="accountSid"/> from SourceSids of the Low IL grant at
    /// <paramref name="path"/>. If SourceSids becomes empty, removes the grant and restores SACL
    /// only when <paramref name="updateFileSystem"/> is true.
    /// No-op if grant has no SourceSids (manually added; not auto-managed).
    /// </summary>
    public void RevertSource(string accountSid, string path, bool updateFileSystem)
    {
        bool removeGrant = false, hadWrite = false;
        string? previousSaclLabel = null;
        DbAccessor.Write(db =>
        {
            var entry = GrantEntryLookup.FindGrantEntryInDb(
                db, AclHelper.LowIntegritySid, path, isDeny: false);
            if (entry?.SourceSids == null) return;
            entry.SourceSids.RemoveAll(s =>
                string.Equals(s, accountSid, StringComparison.OrdinalIgnoreCase));
            if (entry.SourceSids.Count > 0) return;
            removeGrant = true;
            hadWrite = entry.SavedRights?.Write == true;
            previousSaclLabel = entry.PreviousSaclLabel;
        });
        if (!removeGrant) return;
        RemoveGrantWithCleanup(
            AclHelper.LowIntegritySid,
            path,
            updateFileSystem,
            onRemoved: updateFileSystem && hadWrite
                ? () => mandatoryLabelService.RestoreMandatoryLabel(path, previousSaclLabel)
                : null);
    }

    /// <summary>
    /// Removes <paramref name="accountSid"/> from all Low IL grant SourceSids.
    /// Grants that become empty are fully cleaned up.
    /// </summary>
    public void RevertAllSources(string accountSid, bool updateFileSystem)
    {
        var paths = DbAccessor.Read(db =>
        {
            var account = db.GetAccount(AclHelper.LowIntegritySid);
            if (account == null) return (List<string>)[];
            return account.Grants
                .Where(e => e.SourceSids?.Contains(accountSid, StringComparer.OrdinalIgnoreCase) == true)
                .Select(e => e.Path)
                .ToList();
        });
        foreach (var path in paths)
            RevertSource(accountSid, path, updateFileSystem);
    }
}

using RunFence.Core.Models;

namespace RunFence.Acl;

public static class TraverseEntryLookup
{
    public static string ResolveStorageOwnerSid(string accountSid)
        => UsesSharedContainerTraverse(accountSid)
            ? AclHelper.AllApplicationPackagesSid
            : accountSid;

    public static string ResolveAclSid(string accountSid)
        => UsesSharedContainerTraverse(accountSid)
            ? AclHelper.AllApplicationPackagesSid
            : accountSid;

    public static List<GrantedPathEntry> GetOrCreateTraverseStore(AppDatabase database, string accountSid)
        => database.GetOrCreateAccount(ResolveStorageOwnerSid(accountSid)).Grants;

    public static List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string accountSid)
        => database.GetAccount(ResolveStorageOwnerSid(accountSid))?.Grants ?? [];

    public static GrantedPathEntry? FindTraverseEntryInDb(
        AppDatabase database,
        string accountSid,
        string normalizedPath,
        bool includeManualSharedEntries = false)
    {
        var matches = GetTraverseStoreOrEmpty(database, accountSid)
            .Where(entry =>
                entry.IsTraverseOnly &&
                string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!UsesSharedContainerTraverse(accountSid))
            return matches.FirstOrDefault();

        var sourceTrackedEntry = matches.FirstOrDefault(entry =>
            EntryAppliesToSid(entry, accountSid, includeManualSharedEntries: false));
        if (sourceTrackedEntry != null)
            return sourceTrackedEntry;

        if (!includeManualSharedEntries)
            return null;

        return matches.FirstOrDefault(entry => entry.SourceSids == null);
    }

    public static GrantedPathEntry? FindTraverseEntryForMutation(
        IReadOnlyList<GrantedPathEntry> entries,
        string accountSid,
        string normalizedPath,
        bool sourceTrackedEntry)
    {
        if (!UsesSharedContainerTraverse(accountSid))
        {
            return entries.FirstOrDefault(entry =>
                entry.IsTraverseOnly &&
                string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        return entries.FirstOrDefault(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
            ((sourceTrackedEntry &&
              entry.SourceSids?.Contains(accountSid, StringComparer.OrdinalIgnoreCase) == true) ||
             (!sourceTrackedEntry && entry.SourceSids == null)));
    }

    public static bool EntryAppliesToSid(
        GrantedPathEntry entry,
        string accountSid,
        bool includeManualSharedEntries)
    {
        if (!UsesSharedContainerTraverse(accountSid))
            return true;

        if (entry.SourceSids == null)
            return includeManualSharedEntries;

        return entry.SourceSids.Contains(accountSid, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizePathForLookup(string path) => Path.GetFullPath(path);

    private static bool UsesSharedContainerTraverse(string accountSid)
        => AclHelper.IsSpecificContainerSid(accountSid);
}

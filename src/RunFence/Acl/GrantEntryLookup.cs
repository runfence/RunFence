using RunFence.Core.Models;

namespace RunFence.Acl;

public static class GrantEntryLookup
{
    public static GrantedPathEntry? FindGrantEntryInDb(
        AppDatabase database,
        string accountSid,
        string normalizedPath,
        bool isDeny)
    {
        var account = database.GetAccount(accountSid);
        return account == null
            ? null
            : FindGrantEntryInList(account.Grants, normalizedPath, isDeny);
    }

    public static GrantedPathEntry? FindGrantEntryInList(
        IReadOnlyList<GrantedPathEntry> entries,
        string normalizedPath,
        bool isDeny)
        => entries.FirstOrDefault(entry =>
            !entry.IsTraverseOnly &&
            entry.IsDeny == isDeny &&
            string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

    public static GrantConflictResult FindNonTraverseGrantConflict(
        IReadOnlyList<GrantedPathEntry> entries,
        string normalizedPath,
        bool isDeny)
    {
        GrantedPathEntry? sameModeEntry = null;
        GrantedPathEntry? oppositeModeEntry = null;

        foreach (var entry in entries)
        {
            if (entry.IsTraverseOnly ||
                !string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.IsDeny == isDeny)
                sameModeEntry ??= entry;
            else
                oppositeModeEntry ??= entry;

            if (sameModeEntry != null && oppositeModeEntry != null)
                break;
        }

        return new GrantConflictResult(sameModeEntry, oppositeModeEntry);
    }
}

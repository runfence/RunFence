using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

internal static class GrantIntentStoreConfigDataBuilder
{
    public static List<AppConfigAccountEntry>? BuildAccounts(
        IGrantIntentStore store,
        AppDatabase database)
    {
        var result = new List<AppConfigAccountEntry>();
        foreach (var account in database.Accounts)
        {
            var entries = store.GetEntries(account.Sid);
            if (entries.Count == 0)
                continue;

            result.Add(new AppConfigAccountEntry
            {
                Sid = account.Sid,
                Grants = entries.Select(entry => entry.Clone()).ToList()
            });
        }

        return result.Count == 0 ? null : result;
    }
}

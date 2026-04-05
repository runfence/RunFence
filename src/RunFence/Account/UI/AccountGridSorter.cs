using RunFence.Core.Models;

namespace RunFence.Account.UI;

/// <summary>
/// Handles sorting of each account grid section (credentials, local accounts, unavailable SIDs).
/// Extracted from AccountGridPopulator to separate the sorting responsibility.
/// </summary>
public class AccountGridSorter(
    IWindowsAccountService windowsAccountService,
    Func<AppDatabase, string?, string> getAccountAppsText)
{
    private const int AppsColumnIndex = 6;
    private const int ProfilePathColumnIndex = 7;
    private const int SidColumnIndex = 8;

    public IEnumerable<CredentialEntry> SortCredentials(PopulateData data, IEnumerable<CredentialEntry> creds)
    {
        if (!data.IsSortActive)
            return creds.OrderBy(c => !(c.IsCurrentAccount || c.IsInteractiveUser))
                .ThenBy(c => !c.IsCurrentAccount) // current first, then interactive
                .ThenBy(c => data.DisplayNameCache.GetValueOrDefault(c.Id, ""), StringComparer.OrdinalIgnoreCase);

        Func<CredentialEntry, string> key = data.SortColumnIndex switch
        {
            AppsColumnIndex => c => getAccountAppsText(data.Database, c.Sid),
            ProfilePathColumnIndex => c => windowsAccountService.GetProfilePath(c.Sid) ?? "",
            SidColumnIndex => c => c.Sid,
            _ => c => data.DisplayNameCache.GetValueOrDefault(c.Id, "")
        };
        return SortByColumn(creds, key, data.SortDescending);
    }

    public IEnumerable<LocalUserAccount> SortLocalAccounts(PopulateData data, IEnumerable<LocalUserAccount> accounts)
    {
        if (!data.IsSortActive)
            return accounts.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase);

        Func<LocalUserAccount, string> key = data.SortColumnIndex switch
        {
            AppsColumnIndex => u => getAccountAppsText(data.Database, u.Sid),
            ProfilePathColumnIndex => u => windowsAccountService.GetProfilePath(u.Sid) ?? "",
            SidColumnIndex => u => u.Sid,
            _ => u => u.Username
        };
        return SortByColumn(accounts, key, data.SortDescending);
    }

    public IEnumerable<string> SortUnavailableSids(PopulateData data, IEnumerable<string> sids)
    {
        if (!data.IsSortActive)
            return sids.OrderBy(s => data.Database.SidNames.GetValueOrDefault(s, s), StringComparer.OrdinalIgnoreCase);

        Func<string, string> key = data.SortColumnIndex switch
        {
            AppsColumnIndex => s => getAccountAppsText(data.Database, s),
            ProfilePathColumnIndex => _ => "",
            SidColumnIndex => s => s,
            _ => s => data.Database.SidNames.GetValueOrDefault(s, s)
        };
        return SortByColumn(sids, key, data.SortDescending);
    }

    private static IOrderedEnumerable<T> SortByColumn<T>(IEnumerable<T> items, Func<T, string> keySelector, bool sortDescending)
        => sortDescending
            ? items.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase);
}
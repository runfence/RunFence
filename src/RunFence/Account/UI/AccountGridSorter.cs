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
    private DataGridView _grid = null!;

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    private int AppsColumnIndex => _grid.Columns["Apps"]?.Index ?? -1;
    private int ProfilePathColumnIndex => _grid.Columns["ProfilePath"]?.Index ?? -1;
    private int SidColumnIndex => _grid.Columns["Sid"]?.Index ?? -1;

    public IEnumerable<CredentialEntry> SortCredentials(PopulateData data, IEnumerable<CredentialEntry> creds)
    {
        if (!data.IsSortActive)
            return creds.OrderBy(c => !(c.IsCurrentAccount || c.IsInteractiveUser))
                .ThenBy(c => !c.IsCurrentAccount) // current first, then interactive
                .ThenBy(c => data.DisplayNameCache.GetValueOrDefault(c.Id, ""), StringComparer.OrdinalIgnoreCase);

        var appsIdx = AppsColumnIndex;
        var profileIdx = ProfilePathColumnIndex;
        var sidIdx = SidColumnIndex;
        Func<CredentialEntry, string> key = data.SortColumnIndex switch
        {
            var i when i == appsIdx && i >= 0 => c => getAccountAppsText(data.Database, c.Sid),
            var i when i == profileIdx && i >= 0 => c => windowsAccountService.GetProfilePath(c.Sid) ?? "",
            var i when i == sidIdx && i >= 0 => c => c.Sid,
            _ => c => data.DisplayNameCache.GetValueOrDefault(c.Id, "")
        };
        return SortByColumn(creds, key, data.SortDescending);
    }

    public IEnumerable<LocalUserAccount> SortLocalAccounts(PopulateData data, IEnumerable<LocalUserAccount> accounts)
    {
        if (!data.IsSortActive)
            return accounts.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase);

        var appsIdx = AppsColumnIndex;
        var profileIdx = ProfilePathColumnIndex;
        var sidIdx = SidColumnIndex;
        Func<LocalUserAccount, string> key = data.SortColumnIndex switch
        {
            var i when i == appsIdx && i >= 0 => u => getAccountAppsText(data.Database, u.Sid),
            var i when i == profileIdx && i >= 0 => u => windowsAccountService.GetProfilePath(u.Sid) ?? "",
            var i when i == sidIdx && i >= 0 => u => u.Sid,
            _ => u => u.Username
        };
        return SortByColumn(accounts, key, data.SortDescending);
    }

    public IEnumerable<string> SortUnavailableSids(PopulateData data, IEnumerable<string> sids)
    {
        if (!data.IsSortActive)
            return sids.OrderBy(s => data.Database.SidNames.GetValueOrDefault(s, s), StringComparer.OrdinalIgnoreCase);

        var appsIdx = AppsColumnIndex;
        var profileIdx = ProfilePathColumnIndex;
        var sidIdx = SidColumnIndex;
        Func<string, string> key = data.SortColumnIndex switch
        {
            var i when i == appsIdx && i >= 0 => s => getAccountAppsText(data.Database, s),
            var i when i == profileIdx && i >= 0 => _ => "",
            var i when i == sidIdx && i >= 0 => s => s,
            _ => s => data.Database.SidNames.GetValueOrDefault(s, s)
        };
        return SortByColumn(sids, key, data.SortDescending);
    }

    private static IOrderedEnumerable<T> SortByColumn<T>(IEnumerable<T> items, Func<T, string> keySelector, bool sortDescending)
        => sortDescending
            ? items.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase);
}

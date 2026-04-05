using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

public record PopulateData(
    AppDatabase Database,
    CredentialStore CredentialStore,
    Dictionary<Guid, string> DisplayNameCache,
    Dictionary<string, string?>? SidResolutions,
    string? InteractiveUserSid,
    bool IsSortActive,
    int SortColumnIndex,
    bool SortDescending);

public class AccountGridPopulator
{
    private readonly IWindowsAccountService _windowsAccountService;
    private readonly ILoggingService _log;
    private readonly AccountGridSorter _sorter;
    private readonly AccountGridSupplementarySections _supplementary;

    private DataGridView _grid = null!;

    private readonly ILocalUserProvider _localUserProvider;

    public AccountGridPopulator(IWindowsAccountService windowsAccountService,
        ILocalUserProvider localUserProvider,
        ILoggingService log,
        AccountGridSupplementarySections supplementarySections)
    {
        _windowsAccountService = windowsAccountService;
        _localUserProvider = localUserProvider;
        _log = log;
        _sorter = new AccountGridSorter(windowsAccountService, GetAccountAppsText);
        _supplementary = supplementarySections;
    }

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public void Build(PopulateData data)
    {
        var localAccounts = GetLocalAccounts();
        var localSidSet = localAccounts.Select(u => u.Sid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ephemeralLookup = data.Database.Accounts
            .Where(a => a.DeleteAfterUtc.HasValue)
            .ToDictionary(a => a.Sid, a => a, StringComparer.OrdinalIgnoreCase);
        var representedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lockIcon = AccountGridHelper.CreateKeyIcon();

        var ephemeralCredRows = new List<CredentialEntry>();
        var ephemeralLocalRows = new List<LocalUserAccount>();

        AddCredentialsSection(data, localAccounts, localSidSet, ephemeralLookup, representedSids, lockIcon, ephemeralCredRows);
        AddLocalAccountsSection(data, localAccounts, localSidSet, ephemeralLookup, representedSids, ephemeralLocalRows, lockIcon);
        _supplementary.AddEphemeralSection(_grid, data, ephemeralCredRows, ephemeralLocalRows, ephemeralLookup, lockIcon);
        _supplementary.AddAppContainersSection(_grid, data.Database);
        _supplementary.AddUnavailableSection(_grid, data, localSidSet, representedSids, lockIcon, _sorter);
    }

    private List<LocalUserAccount> GetLocalAccounts()
    {
        try
        {
            return _localUserProvider.GetLocalUserAccounts();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to enumerate local accounts", ex);
            return [];
        }
    }

    private void AddCredentialsSection(
        PopulateData data,
        List<LocalUserAccount> localAccounts,
        HashSet<string> localSidSet,
        Dictionary<string, AccountEntry> ephemeralLookup,
        HashSet<string> representedSids,
        Image lockIcon,
        List<CredentialEntry> ephemeralCredRows)
    {
        AccountGridHelper.AddGroupHeaderRow(_grid, "Credentials");

        // Interactive user with no stored credential: show in Credentials section regardless.
        // Pre-compute using the credential store directly (representedSids is not yet populated at this point).
        var syntheticInteractiveSid = !string.IsNullOrEmpty(data.InteractiveUserSid)
                                      && !ephemeralLookup.ContainsKey(data.InteractiveUserSid)
                                      && !data.CredentialStore.Credentials.Any(c =>
                                          string.Equals(c.Sid, data.InteractiveUserSid, StringComparison.OrdinalIgnoreCase))
            ? data.InteractiveUserSid
            : null;

        string? syntheticUsername = null;
        AccountGridSupplementarySections.AccountState syntheticState = default;
        bool syntheticIsKnown = false, syntheticCanImport = false;
        string? syntheticAppsText = null, syntheticProfilePath = null;

        if (syntheticInteractiveSid != null)
        {
            var localAccount = localAccounts.FirstOrDefault(u =>
                string.Equals(u.Sid, syntheticInteractiveSid, StringComparison.OrdinalIgnoreCase));
            syntheticUsername = localAccount?.Username
                                ?? SidNameResolver.ExtractUsername(data.Database.SidNames.GetValueOrDefault(syntheticInteractiveSid, syntheticInteractiveSid));
            syntheticState = _supplementary.LookupAccountState(syntheticInteractiveSid, syntheticUsername, data.InteractiveUserSid);
            syntheticIsKnown = localSidSet.Contains(syntheticInteractiveSid);
            syntheticCanImport = SidResolutionHelper.CanLaunchWithoutPassword(syntheticInteractiveSid);
            syntheticAppsText = GetAccountAppsText(data.Database, syntheticInteractiveSid);
            syntheticProfilePath = _windowsAccountService.GetProfilePath(syntheticInteractiveSid) ?? "";
        }

        bool syntheticRendered = syntheticInteractiveSid == null;

        void RenderSyntheticRow()
        {
            var logonValue = !syntheticIsKnown || syntheticState.NoLogonState == false;
            var allowInternet = data.Database.GetAccount(syntheticInteractiveSid!)?.Firewall.AllowInternet ?? true;
            var idx = _grid.Rows.Add(false, lockIcon, syntheticUsername + " (interactive)", logonValue, allowInternet, syntheticAppsText!, syntheticProfilePath!,
                syntheticInteractiveSid!);
            var row = _grid.Rows[idx];
            row.Tag = new AccountRow(null, syntheticUsername!, syntheticInteractiveSid!, false);
            row.Cells["Credential"].ToolTipText = "No Password";
            row.Cells["SID"].ToolTipText = syntheticInteractiveSid;
            row.Cells["Import"].ReadOnly = !syntheticCanImport;
            row.Cells["Account"].ReadOnly = false;
            AccountGridSupplementarySections.SetLogonCellState(row, syntheticState);
            if (!syntheticIsKnown)
                row.Cells["Logon"].ReadOnly = true;
            if (!syntheticCanImport)
                row.Cells["Import"].ToolTipText = "No password stored for this account";
            row.Cells["Logon"].ToolTipText = "Cannot change Logon for the interactive desktop user";
            representedSids.Add(syntheticInteractiveSid!);
            syntheticRendered = true;
        }

        foreach (var cred in _sorter.SortCredentials(data, data.CredentialStore.Credentials))
        {
            if (!string.IsNullOrEmpty(cred.Sid) && !cred.IsCurrentAccount
                                                && ephemeralLookup.ContainsKey(cred.Sid) && localSidSet.Contains(cred.Sid))
            {
                representedSids.Add(cred.Sid);
                ephemeralCredRows.Add(cred);
                continue;
            }

            // For default sort: insert synthetic at interactive-user priority (after current account, before others).
            if (!syntheticRendered && !data.IsSortActive && !cred.IsCurrentAccount)
                RenderSyntheticRow();

            var hasStoredPassword = cred.IsCurrentAccount || cred.IsInteractiveUser || cred.EncryptedPassword.Length > 0;
            var username = cred.IsCurrentAccount
                ? Environment.UserName
                : SidNameResolver.ExtractUsername(data.Database.SidNames.GetValueOrDefault(cred.Sid, cred.Sid));
            var isKnown = !string.IsNullOrEmpty(cred.Sid) && localSidSet.Contains(cred.Sid);
            var canImport = hasStoredPassword || cred.IsCurrentAccount;
            var state = _supplementary.LookupAccountState(cred.Sid, username, data.InteractiveUserSid);

            var displayName = data.DisplayNameCache[cred.Id];
            if (state.IsInteractive && !cred.IsInteractiveUser)
                displayName += " (interactive)";

            var accountRow = new AccountRow(cred, username, cred.Sid, hasStoredPassword);
            Image credIcon = hasStoredPassword ? lockIcon : AccountGridHelper.EmptyIcon;
            var appsText = GetAccountAppsText(data.Database, cred.Sid);
            var profilePath = _windowsAccountService.GetProfilePath(cred.Sid) ?? "";
            var logonValue = !isKnown || state.NoLogonState == false;
            var allowInternet = data.Database.GetAccount(cred.Sid)?.Firewall.AllowInternet ?? true;
            var idx = _grid.Rows.Add(false, credIcon, displayName, logonValue, allowInternet, appsText, profilePath, cred.Sid);
            var row = _grid.Rows[idx];
            row.Tag = accountRow;
            row.Cells["Credential"].ToolTipText = hasStoredPassword ? "Stored" : "No Password";
            row.Cells["SID"].ToolTipText = cred.Sid;
            row.Cells["Import"].ReadOnly = !canImport;
            row.Cells["Account"].ReadOnly = false;
            AccountGridSupplementarySections.SetLogonCellState(row, state);
            if (!isKnown)
                row.Cells["Logon"].ReadOnly = true;

            if (!canImport)
                row.Cells["Import"].ToolTipText = "No password stored for this account";
            if (state.IsInteractive)
                row.Cells["Logon"].ToolTipText = "Cannot change Logon for the interactive desktop user";

            if (!string.IsNullOrEmpty(cred.Sid))
                representedSids.Add(cred.Sid);
        }

        // Append synthetic after all sorted credentials (covers: no credentials at all, or column sort active).
        if (!syntheticRendered)
            RenderSyntheticRow();
    }

    private void AddLocalAccountsSection(
        PopulateData data,
        List<LocalUserAccount> localAccounts,
        HashSet<string> localSidSet,
        Dictionary<string, AccountEntry> ephemeralLookup,
        HashSet<string> representedSids,
        List<LocalUserAccount> ephemeralLocalRows,
        Image lockIcon)
    {
        var localOnlyAccounts = new List<LocalUserAccount>();
        foreach (var localUser in localAccounts.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
        {
            if (representedSids.Contains(localUser.Sid))
                continue;

            if (ephemeralLookup.ContainsKey(localUser.Sid))
            {
                representedSids.Add(localUser.Sid);
                ephemeralLocalRows.Add(localUser);
                continue;
            }

            try
            {
                if (_windowsAccountService.IsAccountDisabled(localUser.Username))
                    continue;
            }
            catch
            {
                /* best effort */
            }

            localOnlyAccounts.Add(localUser);
        }

        if (localOnlyAccounts.Count == 0)
            return;

        AccountGridHelper.AddGroupHeaderRow(_grid, "Local Accounts");

        foreach (var localUser in _sorter.SortLocalAccounts(data, localOnlyAccounts))
        {
            var state = _supplementary.LookupAccountState(localUser.Sid, localUser.Username, data.InteractiveUserSid);
            var accountRow = new AccountRow(null, localUser.Username, localUser.Sid, false);
            var localAppsText = GetAccountAppsText(data.Database, localUser.Sid);
            var localProfilePath = _windowsAccountService.GetProfilePath(localUser.Sid) ?? "";
            var displayName = state.IsInteractive ? localUser.Username + " (interactive)" : localUser.Username;
            var localAllowInternet = data.Database.GetAccount(localUser.Sid)?.Firewall.AllowInternet ?? true;
            Image localCredIcon = state.IsInteractive ? lockIcon : AccountGridHelper.EmptyIcon;
            var idx = _grid.Rows.Add(false, localCredIcon, displayName, state.NoLogonState == false, localAllowInternet, localAppsText, localProfilePath, localUser.Sid);
            var row = _grid.Rows[idx];
            row.Cells["SID"].ToolTipText = localUser.Sid;
            row.Tag = accountRow;
            row.Cells["Import"].ReadOnly = !state.IsInteractive;
            AccountGridSupplementarySections.SetLogonCellState(row, state);
            if (!state.IsInteractive)
                row.Cells["Import"].ToolTipText = "No credentials stored \u2014 use Edit to add";
            if (state.IsInteractive)
                row.Cells["Logon"].ToolTipText = "Cannot change Logon for the interactive desktop user";
            representedSids.Add(localUser.Sid);
        }
    }

    private string GetAccountAppsText(AppDatabase database, string? sid)
        => _supplementary.GetAccountAppsText(database, sid);
}
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;

namespace RunFence.Account.UI;

/// <summary>
/// Builds the Ephemeral, Unavailable, and App Containers grid sections for the accounts grid.
/// Extracted from <see cref="AccountGridPopulator"/> to keep each section independently maintainable.
/// </summary>
public class AccountGridSupplementarySections(
    IWindowsAccountService windowsAccountService,
    IAccountLoginRestrictionService accountRestriction,
    SidDisplayNameResolver displayNameResolver)
{
    public void AddEphemeralSection(
        DataGridView grid,
        PopulateData data,
        List<CredentialEntry> ephemeralCredRows,
        List<LocalUserAccount> ephemeralLocalRows,
        Dictionary<string, AccountEntry> ephemeralLookup,
        Image lockIcon)
    {
        if (ephemeralCredRows.Count == 0 && ephemeralLocalRows.Count == 0)
            return;

        AccountGridHelper.AddGroupHeaderRow(grid, "Ephemeral Accounts");

        foreach (var cred in ephemeralCredRows)
        {
            var hasStoredPassword = cred.IsInteractiveUser || cred.EncryptedPassword.Length > 0;
            var username = SidNameResolver.ExtractUsername(data.Database.SidNames.GetValueOrDefault(cred.Sid, cred.Sid));
            var state = LookupAccountState(cred.Sid, username, data.InteractiveUserSid);
            var displayName = data.DisplayNameCache[cred.Id];
            if (state.IsInteractive && !cred.IsInteractiveUser)
                displayName += " (interactive)";
            var entry = ephemeralLookup[cred.Sid];
            var localExpiry = entry.DeleteAfterUtc!.Value.ToLocalTime();
            var expiryTooltip = entry.DeleteAfterUtc > DateTime.UtcNow
                ? $"Expires: {localExpiry:g}"
                : "Expired (deletion postponed)";
            var accountRow = new AccountRow(cred, username, cred.Sid, hasStoredPassword, isEphemeral: true);
            Image credIcon = hasStoredPassword ? lockIcon : AccountGridHelper.EmptyIcon;
            var appsText = GetAccountAppsText(data.Database, cred.Sid);
            var profilePath = windowsAccountService.GetProfilePath(cred.Sid) ?? "";
            var ephCredAllowInternet = data.Database.GetAccount(cred.Sid)?.Firewall.AllowInternet ?? true;
            var idx = grid.Rows.Add(false, credIcon, displayName, state.NoLogonState == false, ephCredAllowInternet, appsText, profilePath, cred.Sid);
            var row = grid.Rows[idx];
            row.Tag = accountRow;
            row.Cells["Account"].ToolTipText = expiryTooltip;
            row.Cells["SID"].ToolTipText = cred.Sid;
            row.Cells["Credential"].ToolTipText = hasStoredPassword ? "Stored" : "No Password";
            row.Cells["Import"].ReadOnly = !hasStoredPassword;
            SetLogonCellState(row, state);
            if (!hasStoredPassword)
                row.Cells["Import"].ToolTipText = "No password stored for this account";
            if (state.IsInteractive)
                row.Cells["Logon"].ToolTipText = "Cannot change Logon for the interactive desktop user";
        }

        foreach (var localUser in ephemeralLocalRows)
        {
            var state = LookupAccountState(localUser.Sid, localUser.Username, data.InteractiveUserSid);
            var entry = ephemeralLookup[localUser.Sid];
            var localExpiry = entry.DeleteAfterUtc!.Value.ToLocalTime();
            var expiryTooltip = entry.DeleteAfterUtc > DateTime.UtcNow
                ? $"Expires: {localExpiry:g}"
                : "Expired (deletion postponed)";
            var displayName = state.IsInteractive ? localUser.Username + " (interactive)" : localUser.Username;
            var accountRow = new AccountRow(null, localUser.Username, localUser.Sid, false, isEphemeral: true);
            var appsText = GetAccountAppsText(data.Database, localUser.Sid);
            var profilePath = windowsAccountService.GetProfilePath(localUser.Sid) ?? "";
            var ephLocalAllowInternet = data.Database.GetAccount(localUser.Sid)?.Firewall.AllowInternet ?? true;
            var idx = grid.Rows.Add(false, AccountGridHelper.EmptyIcon, displayName, state.NoLogonState == false, ephLocalAllowInternet, appsText, profilePath, localUser.Sid);
            var row = grid.Rows[idx];
            row.Tag = accountRow;
            row.Cells["Account"].ToolTipText = expiryTooltip;
            row.Cells["SID"].ToolTipText = localUser.Sid;
            row.Cells["Import"].ReadOnly = true;
            row.Cells["Import"].ToolTipText = "No credentials stored \u2014 use Edit to add";
            SetLogonCellState(row, state);
            if (state.IsInteractive)
                row.Cells["Logon"].ToolTipText = "Cannot change Logon for the interactive desktop user";
        }
    }

    public void AddUnavailableSection(
        DataGridView grid,
        PopulateData data,
        HashSet<string> localSidSet,
        HashSet<string> representedSids,
        Image lockIcon,
        AccountGridSorter sorter)
    {
        var currentUserSid = SidResolutionHelper.GetCurrentUserSid();

        var unavailableSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in data.Database.SidNames.Keys)
        {
            if (representedSids.Contains(sid))
                continue;
            if (string.Equals(sid, currentUserSid, StringComparison.OrdinalIgnoreCase))
                continue;
            if (localSidSet.Contains(sid))
                continue;
            if (data.SidResolutions != null && data.SidResolutions.TryGetValue(sid, out var resolved) && resolved != null)
                continue;
            unavailableSids.Add(sid);
        }

        foreach (var cred in data.CredentialStore.Credentials.Where(c => !string.IsNullOrEmpty(c.Sid)))
        {
            if (representedSids.Contains(cred.Sid))
                continue;
            if (string.Equals(cred.Sid, currentUserSid, StringComparison.OrdinalIgnoreCase))
                continue;
            if (localSidSet.Contains(cred.Sid))
                continue;
            if (data.SidResolutions != null && data.SidResolutions.TryGetValue(cred.Sid, out var resolved) && resolved != null)
                continue;
            unavailableSids.Add(cred.Sid);
        }

        var credentialSids = data.CredentialStore.Credentials
            .Where(c => !string.IsNullOrEmpty(c.Sid))
            .Select(c => c.Sid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appAccountSids = data.Database.Apps
            .Select(a => a.AccountSid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ipcCallerSids = data.Database.Accounts
            .Where(a => a.IsIpcCaller).Select(a => a.Sid)
            .Concat(data.Database.Apps
                .Where(a => a.AllowedIpcCallers != null)
                .SelectMany(a => a.AllowedIpcCallers!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowAclSids = data.Database.Apps
            .Where(a => a.AllowedAclEntries != null)
            .SelectMany(a => a.AllowedAclEntries!).Select(e => e.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        unavailableSids.RemoveWhere(sid =>
            !credentialSids.Contains(sid) &&
            !appAccountSids.Contains(sid) &&
            !ipcCallerSids.Contains(sid) &&
            !allowAclSids.Contains(sid));

        if (unavailableSids.Count == 0)
            return;

        AccountGridHelper.AddGroupHeaderRow(grid, "Unavailable Accounts");

        foreach (var sid in sorter.SortUnavailableSids(data, unavailableSids))
        {
            var mapName = data.Database.SidNames.GetValueOrDefault(sid, sid);
            var displayName = displayNameResolver.GetDisplayName(sid, null, data.Database.SidNames);
            var cred = data.CredentialStore.Credentials.FirstOrDefault(c =>
                string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));
            var hasStoredPassword = cred is { EncryptedPassword.Length: > 0 };
            var accountRow = new AccountRow(cred, SidNameResolver.ExtractUsername(mapName), sid, hasStoredPassword, isUnavailable: true);
            Image credIcon = hasStoredPassword ? lockIcon : AccountGridHelper.EmptyIcon;
            var unavailableAppsText = GetAccountAppsText(data.Database, sid);
            var idx = grid.Rows.Add(false, credIcon, displayName, true, true, unavailableAppsText, "", sid);
            var row = grid.Rows[idx];
            row.Tag = accountRow;
            row.Cells["SID"].ToolTipText = sid;
            row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            foreach (DataGridViewCell cell in row.Cells)
                cell.ReadOnly = true;
        }
    }

    public void AddAppContainersSection(DataGridView grid, AppDatabase database)
    {
        if (database.AppContainers.Count == 0)
            return;

        AccountGridHelper.AddGroupHeaderRow(grid, "App Containers");

        var containerIcon = AccountGridHelper.CreateContainerIcon();
        foreach (var container in database.AppContainers.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var containerSid = container.Sid;
            var appsText = GetContainerAppsText(database, container.Name);
            var dataPath = AppContainerPaths.GetContainerDataPath(container.Name);
            var displayName = container.DisplayName;
            if (container.IsEphemeral)
                displayName += " (ephemeral)";

            var idx = grid.Rows.Add(false, containerIcon, displayName,
                true, true, appsText, dataPath, containerSid);
            var row = grid.Rows[idx];
            row.Tag = new ContainerRow(container, containerSid);
            row.Cells["SID"].ToolTipText = containerSid;
            row.Cells["Credential"].ToolTipText = "App Container";

            foreach (DataGridViewCell cell in row.Cells)
                cell.ReadOnly = true;

            foreach (var colName in new[] { "Import", "Logon" })
            {
                var colIndex = grid.Columns[colName]?.Index;
                if (colIndex.HasValue)
                    row.Cells[colIndex.Value] = new DataGridViewTextBoxCell { Value = "" };
            }

            var internetColIndex = grid.Columns["colAllowInternet"]?.Index;
            if (internetColIndex.HasValue)
            {
                var cell = (DataGridViewCheckBoxCell)row.Cells[internetColIndex.Value];
                var internetState = GetContainerInternetState(container);
                if (internetState == CheckState.Indeterminate)
                {
                    cell.ThreeState = true;
                    cell.Value = CheckState.Indeterminate;
                }
                else
                {
                    cell.Value = internetState == CheckState.Checked;
                }

                cell.ReadOnly = false;
            }

            if (container is { IsEphemeral: true, DeleteAfterUtc: not null })
            {
                var localExpiry = container.DeleteAfterUtc.Value.ToLocalTime();
                var tooltip = container.DeleteAfterUtc.Value > DateTime.UtcNow
                    ? $"Expires: {localExpiry:g}"
                    : "Expired (deletion pending)";
                row.Cells["Account"].ToolTipText = tooltip;
            }
        }
    }

    public record struct AccountState(bool? NoLogonState, bool IsInteractive);

    public AccountState LookupAccountState(string sid, string? username, string? interactiveSid) => new(
        accountRestriction.GetNoLogonState(sid, username),
        !string.IsNullOrEmpty(interactiveSid) && string.Equals(sid, interactiveSid, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Configures the Logon cell: sets ReadOnly for interactive users,
    /// and enables ThreeState + Indeterminate display for partial states.
    /// Must be called AFTER Rows.Add (which sets a bool placeholder).
    /// </summary>
    public static void SetLogonCellState(DataGridViewRow row, AccountState state)
    {
        var cell = (DataGridViewCheckBoxCell)row.Cells["Logon"];
        cell.ReadOnly = state is { IsInteractive: true, NoLogonState: false };
        if (state.NoLogonState == null)
        {
            cell.ThreeState = true;
            cell.Value = CheckState.Indeterminate;
        }
    }

    public string GetAccountAppsText(AppDatabase database, string? sid)
        => string.Join(", ", database.Apps
            .Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => a.Name));

    private string GetContainerAppsText(AppDatabase database, string containerName)
        => string.Join(", ", database.Apps
            .Where(a => string.Equals(a.AppContainerName, containerName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => a.Name));

    private static CheckState GetContainerInternetState(AppContainerEntry container)
    {
        var caps = container.Capabilities ?? [];
        var count = AccountContainerOrchestrator.InternetCapabilitySids
            .Count(sid => caps.Contains(sid, StringComparer.OrdinalIgnoreCase));
        return count == AccountContainerOrchestrator.InternetCapabilitySids.Length ? CheckState.Checked
            : count == 0 ? CheckState.Unchecked
            : CheckState.Indeterminate;
    }
}
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Handles credential CRUD operations for the accounts grid: add, edit, and remove credential.
/// Uses <see cref="CredentialDialogHelper"/> for the add/edit dialogs.
/// </summary>
public class AccountCredentialCrudHandler(
    ISessionProvider sessionProvider,
    IEvaluationLimitHelper evaluationLimitHelper,
    ILocalUserProvider localUserProvider,
    CredentialDialogHelper credentialDialogHelper,
    IAccountCredentialManager credentialManager,
    ISidNameCacheService sidNameCache,
    ISidResolver sidResolver,
    IProfilePathResolver profilePathResolver)
{
    /// <summary>Raised when the credential operations need to open the create user dialog.</summary>
    public event Action<string?, ProtectedString?>? CreateUserDialogRequested;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    /// <summary>
    /// Opens the add credential dialog. <paramref name="selectedRow"/> is the currently selected row,
    /// used to pre-populate the username field. Pass null if nothing is selected.
    /// </summary>
    public void AddCredential(AccountRow? selectedRow)
    {
        var session = sessionProvider.GetSession();
        if (!evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
                extraMessage: "Right-click any credential in the list to remove it."))
            return;

        // Interactive user without stored credentials is already in the Credentials section;
        // redirect to Edit flow which opens an add-mode dialog without the existingSids block.
        if (selectedRow is { Credential: null }
            && SidResolutionHelper.IsInteractiveUserSid(selectedRow.Sid))
        {
            EditCredential(selectedRow);
            return;
        }

        var existingSids = session.CredentialStore.Credentials.Select(c => c.Sid).ToList();
        // Also include the interactive user SID to prevent adding a duplicate credential
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null && !existingSids.Contains(interactiveSid, StringComparer.OrdinalIgnoreCase))
            existingSids.Add(interactiveSid);

        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        var allLocalUsers = localUserProvider.GetLocalUserAccounts();
        // Filter dropdown to only show accounts that can actually have a credential added:
        // exclude accounts already with credentials, the interactive user, and the current account.
        var availableLocalUsers = allLocalUsers
            .Where(u => !existingSids.Any(s => string.Equals(s, u.Sid, StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(u.Sid, currentSid, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Pre-populate username from the currently selected account row, if applicable.
        string? defaultUsername = null;
        if (selectedRow is { IsUnavailable: false, Credential: null }
            && !string.Equals(selectedRow.Sid, currentSid, StringComparison.OrdinalIgnoreCase)
            && !existingSids.Any(s => string.Equals(s, selectedRow.Sid, StringComparison.OrdinalIgnoreCase)))
        {
            defaultUsername = selectedRow.Username;
        }

        var dialogResult = credentialDialogHelper.ShowAddDialog(
            availableLocalUsers, defaultUsername,
            sessionProvider.GetSession().Database.SidNames, existingSids);

        if (dialogResult.Result == DialogResult.Retry && dialogResult.OpenCreateUser)
        {
            CreateUserDialogRequested?.Invoke(dialogResult.Username, dialogResult.CapturedPassword);
            return;
        }

        if (dialogResult.Result != DialogResult.OK)
            return;

        try
        {
            AddNewCredential(dialogResult.Sid!, dialogResult.Username, dialogResult.Password);
        }
        finally
        {
            dialogResult.Password?.Dispose();
        }
    }

    /// <summary>
    /// Opens the edit credential dialog for <paramref name="accountRow"/>.
    /// Caller must ensure the row is selected and not unavailable.
    /// </summary>
    public void EditCredential(AccountRow accountRow)
    {
        if (accountRow.IsUnavailable)
            return;

        if (accountRow.Credential?.IsCurrentAccount == true)
        {
            MessageBox.Show("The current account entry cannot be edited.",
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (accountRow.Credential != null)
        {
            var credEntry = accountRow.Credential;
            var localUsers = localUserProvider.GetLocalUserAccounts();

            var dialogResult = credentialDialogHelper.ShowEditDialog(
                credEntry, localUsers,
                sessionProvider.GetSession().Database.SidNames);

            if (dialogResult.Result != DialogResult.OK)
                return;

            try
            {
                var session = sessionProvider.GetSession();
                if (dialogResult.Password != null)
                    credentialManager.UpdateCredentialPassword(credEntry, dialogResult.Password, session.PinDerivedKey);

                SaveAndRefreshRequested?.Invoke(credEntry.Id, -1);
            }
            finally
            {
                dialogResult.Password?.Dispose();
            }
        }
        else
        {
            var session = sessionProvider.GetSession();
            var localUsers = localUserProvider.GetLocalUserAccounts();
            var existingSids = session.CredentialStore.Credentials.Select(c => c.Sid).ToList();

            var dialogResult = credentialDialogHelper.ShowAddDialog(
                localUsers, accountRow.Username,
                session.Database.SidNames, existingSids);

            if (dialogResult.Result != DialogResult.OK)
                return;

            try
            {
                AddNewCredential(dialogResult.Sid!, dialogResult.Username, dialogResult.Password);
            }
            finally
            {
                dialogResult.Password?.Dispose();
            }
        }
    }

    /// <summary>
    /// Removes the credential for <paramref name="accountRow"/>.
    /// <paramref name="selectedIndex"/> is used to restore grid selection after removal.
    /// Caller must ensure the row is not unavailable.
    /// </summary>
    public void RemoveCredential(AccountRow accountRow, int selectedIndex)
    {
        if (accountRow.IsUnavailable)
            return;

        if (accountRow.Credential == null)
            return;

        var credEntry = accountRow.Credential;
        if (credEntry.IsCurrentAccount)
        {
            MessageBox.Show("The current account entry cannot be removed.",
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var session = sessionProvider.GetSession();
        var displayName = SidNameResolver.GetDisplayName(credEntry, sidResolver, session.Database.SidNames, profilePathResolver);
        var usedBy = session.Database.Apps.Where(a =>
            string.Equals(a.AccountSid, credEntry.Sid, StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();

        var confirmMessage = usedBy.Count > 0
            ? $"Remove credential '{displayName}'?\n\nThis credential is used by: {string.Join(", ", usedBy)}\nThose apps will fail to launch until a new credential is added."
            : $"Remove credential '{displayName}'?";

        if (MessageBox.Show(confirmMessage, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            credentialManager.RemoveCredential(credEntry.Id, session.CredentialStore);
            SaveAndRefreshRequested?.Invoke(null, selectedIndex);
        }
    }

    private void AddNewCredential(string sid, string username, ProtectedString? password)
    {
        var session = sessionProvider.GetSession();
        if (!evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
                extraMessage: "Right-click any credential in the list to remove it."))
            return;

        var (success, credId, error) = credentialManager.AddNewCredential(
            sid, password,
            session.CredentialStore, session.PinDerivedKey);

        if (!success)
        {
            MessageBox.Show(error!, "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        sidNameCache.ResolveAndCache(sid, username);

        SaveAndRefreshRequested?.Invoke(credId, -1);
    }
}

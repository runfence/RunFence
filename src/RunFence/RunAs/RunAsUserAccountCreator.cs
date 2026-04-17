using RunFence.Account;
using RunFence.Account.UI.Forms;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;

namespace RunFence.RunAs;

/// <summary>
/// Handles the "Create New Account" flow from the RunAs dialog.
/// Creates the account, encrypts the password, stores credentials, and optionally prompts
/// for permission grant.
/// </summary>
public class RunAsUserAccountCreator(
    IAppStateProvider appState,
    IDataChangeNotifier dataChangeNotifier,
    SessionContext session,
    ICredentialEncryptionService encryptionService,
    IDatabaseService databaseService,
    ILocalUserProvider localUserProvider,
    ILoggingService log,
    RunAsAccountSettingsApplier settingsApplier,
    RunAsAccountCreationUI creationUi,
    ISidNameCacheService sidNameCache,
    RunAsPermissionPromptHelper permissionPromptHelper,
    IModalCoordinator modalCoordinator)
{
    /// <summary>
    /// Creates the account, encrypts the password, stores credentials, and optionally prompts
    /// for permission grant. Returns the new credential on success, or null if cancelled/failed.
    /// </summary>
    public CredentialEntry? CreateNewAccount(string filePath, RunAsDosProtection dosProtection,
        out AncestorPermissionResult? permissionGrant)
    {
        permissionGrant = null;

        var createResult = creationUi.ShowCreateAccountDialog(filePath, dosProtection);
        if (createResult.WasCancelled)
            return null;

        EditAccountDialog createDlg = createResult.Dialog!;
        try
        {
            CredentialEntry newCredential;
            {
                using var scope = session.PinDerivedKey.Unprotect();
                var encryptedPassword = encryptionService.Encrypt(
                    createDlg.CreatedPassword!, scope.Data);

                newCredential = new CredentialEntry
                {
                    Id = Guid.NewGuid(),
                    Sid = createDlg.CreatedSid!,
                    EncryptedPassword = encryptedPassword
                };
                sidNameCache.ResolveAndCache(createDlg.CreatedSid!, createDlg.NewUsername!);

                session.CredentialStore.Credentials.Add(newCredential);
                localUserProvider.InvalidateCache();
                try
                {
                    databaseService.SaveCredentialStore(session.CredentialStore);
                }
                catch (Exception saveEx)
                {
                    log.Error("RunAsUserAccountCreator: failed to save credential store — scheduling ephemeral cleanup", saveEx);
                    appState.Database.GetOrCreateAccount(createDlg.CreatedSid!).DeleteAfterUtc = DateTime.UtcNow.AddHours(1);
                    throw;
                }

                if (createDlg.IsEphemeral)
                    appState.Database.GetOrCreateAccount(createDlg.CreatedSid!).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

                // Apply privilege level default and firewall DB settings
                settingsApplier.ApplyLaunchDefaults(createDlg.CreatedSid!,
                    createDlg.SelectedPrivilegeLevel);

                if (createDlg.FirewallSettingsChanged)
                    settingsApplier.ApplyFirewallDbSettings(createDlg.CreatedSid!,
                        createDlg.AllowInternet, createDlg.AllowLocalhost, createDlg.AllowLan);
            }

            settingsApplier.RunPostCreationTasks(
                createDlg.CreatedSid!, createDlg.NewUsername!,
                createDlg.SettingsImportPath,
                createDlg.FirewallSettingsChanged, createDlg.Errors);

            // Permission check for the new account
            try
            {
                permissionGrant = permissionPromptHelper.PromptIfNeeded(filePath, newCredential.Sid);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (createDlg.Errors.Count > 0)
            {
                var errorMsg = string.Join("\n", createDlg.Errors);
                MessageBox.Show($"Account created with warnings:\n\n{errorMsg}",
                    "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            dataChangeNotifier.NotifyDataChanged();
            return newCredential;
        }
        finally
        {
            createDlg.CreatedPassword?.Dispose();
            createDlg.Dispose();
            modalCoordinator.EndModal();
        }
    }
}

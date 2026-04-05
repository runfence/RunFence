using System.Security.AccessControl;
using RunFence.Account;
using RunFence.Account.UI.Forms;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.UI.Forms;

namespace RunFence.RunAs;

/// <summary>
/// Handles the "Create New Account" and "Create New Container" flows from the RunAs dialog.
/// Extracted from RunAsAppEntryManager to keep it focused on app entry persistence.
/// </summary>
public class RunAsAccountCreator(
    IAppStateProvider appState,
    IDataChangeNotifier dataChangeNotifier,
    SessionContext session,
    ICredentialEncryptionService encryptionService,
    IDatabaseService databaseService,
    ILocalUserProvider localUserProvider,
    IAclPermissionService aclPermission,
    ILoggingService log,
    RunAsAccountSettingsApplier settingsApplier,
    RunAsAccountCreationUI creationUi,
    ISidNameCacheService sidNameCache,
    ILicenseService licenseService)
{
    /// <summary>
    /// Handles the "Create New Account" flow from the RunAs dialog.
    /// Creates the account, encrypts the password, stores credentials, and optionally prompts
    /// for permission grant. Returns the new credential on success, or null if cancelled/failed.
    /// </summary>
    public CredentialEntry? CreateNewAccount(string filePath, RunAsDosProtection dosProtection,
        out AncestorPermissionResult? permissionGrant)
    {
        permissionGrant = null;

        DataPanel.BeginModal();
        EditAccountDialog? createDlg = null;
        try
        {
            createDlg = creationUi.ShowCreateAccountDialog(filePath, dosProtection);
            if (createDlg == null)
                return null;

            CredentialEntry newCredential;
            {
                using var scope = session.PinDerivedKey.Unprotect();
                var encryptedPassword = encryptionService.Encrypt(
                    createDlg.CreatedPassword!, scope.Data);

                if (!EvaluationLimitHelper.CheckCredentialLimit(licenseService, session.CredentialStore.Credentials))
                {
                    dosProtection.RecordDecline();
                    return null;
                }

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
                    log.Error("RunAsAccountCreator: failed to save credential store — scheduling ephemeral cleanup", saveEx);
                    appState.Database.GetOrCreateAccount(createDlg.CreatedSid!).DeleteAfterUtc = DateTime.UtcNow.AddHours(1);
                    throw;
                }

                if (createDlg.IsEphemeral)
                    appState.Database.GetOrCreateAccount(createDlg.CreatedSid!).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

                // Apply split-token/low-integrity defaults and firewall DB settings
                settingsApplier.ApplyLaunchDefaults(createDlg.CreatedSid!,
                    createDlg.UseSplitTokenDefault, createDlg.UseLowIntegrityDefault);

                if (createDlg.FirewallSettingsChanged)
                    settingsApplier.ApplyFirewallDbSettings(createDlg.CreatedSid!,
                        createDlg.AllowInternet, createDlg.AllowLocalhost, createDlg.AllowLan);
            }

            try
            {
                settingsApplier.RunPostCreationTasks(
                    createDlg.CreatedSid!, createDlg.NewUsername!,
                    createDlg.SettingsImportPath, createDlg.CreatedPassword,
                    createDlg.FirewallSettingsChanged, createDlg.Errors);
            }
            finally
            {
                createDlg.CreatedPassword?.Dispose();
            }

            // Permission check for the new account
            if (aclPermission.NeedsPermissionGrantOrParent(filePath, newCredential.Sid))
            {
                var ancestors = aclPermission.GetGrantableAncestors(filePath);
                if (ancestors.Count > 0)
                {
                    try
                    {
                        permissionGrant = AclPermissionDialogHelper.ShowAncestorPermissionDialog(
                            null, "Missing permissions", ancestors, FileSystemRights.ReadAndExecute);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                }
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
            createDlg?.Dispose();
            DataPanel.EndModal();
        }
    }

    /// <summary>
    /// Opens AppContainerEditDialog for inline container creation from the RunAs flow.
    /// Saves the new container to the database and returns it, or null if cancelled.
    /// </summary>
    public AppContainerEntry? CreateNewContainer()
    {
        if (!licenseService.CanCreateContainer(appState.Database.AppContainers.Count))
        {
            MessageBox.Show(licenseService.GetRestrictionMessage(EvaluationFeature.Containers, appState.Database.AppContainers.Count),
                "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var newContainer = creationUi.ShowCreateContainerDialog();
        if (newContainer == null)
            return null;

        using var scope = session.PinDerivedKey.Unprotect();
        databaseService.SaveConfig(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
        dataChangeNotifier.NotifyDataChanged();
        return newContainer;
    }
}
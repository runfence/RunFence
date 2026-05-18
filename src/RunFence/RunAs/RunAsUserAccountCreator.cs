using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.RunAs;

/// <summary>
/// Handles the "Create New Account" flow from the RunAs dialog.
/// Creates the account, persists credentials, and optionally prompts for permission grant.
/// </summary>
public class RunAsUserAccountCreator(
    IDataChangeNotifier dataChangeNotifier,
    SessionContext session,
    RunAsCreatedAccountPersistenceCoordinator persistenceCoordinator,
    RunAsCreatedAccountPostSetupService postSetupService,
    IRunAsAccountCreationUI creationUi,
    RunAsAccountCreationErrorPresenter errorPresenter,
    IModalCoordinator modalCoordinator) : IRunAsUserAccountCreator
{
    /// <summary>
    /// Creates the account, encrypts the password, stores credentials, and optionally prompts
    /// for permission grant. Returns the new credential on success, or null if cancelled/failed.
    /// </summary>
    public async Task<RunAsCreatedAccountResult?> CreateNewAccountAsync(string filePath)
    {
        var createResult = creationUi.ShowCreateAccountDialog(filePath);
        if (createResult.WasCancelled)
            return null;

        var createDlg = createResult.Dialog
            ?? throw new InvalidOperationException("Missing create-account dialog result.");
        try
        {
            var persistenceRequest = new RunAsCreatedAccountPersistenceRequest
            {
                CreatedSid = createDlg.CreatedSid,
                Username = createDlg.NewUsername,
                CreatedPassword = createDlg.CreatedPassword,
                CreatedRollbackState = createDlg.CreatedRollbackState,
                CreatedAccountStatus = createDlg.CreatedAccountStatus,
                CreatedAccountErrorMessage = createResult.ErrorMessage,
                ScheduleEphemeralCleanupOnRollbackFailure = true
            };
            var postSetupRequest = new RunAsCreatedAccountPostSetupRequest
            {
                CreatedSid = createDlg.CreatedSid,
                Username = createDlg.NewUsername,
                FilePath = filePath,
                Errors = [.. createDlg.Errors],
                IsEphemeral = createDlg.IsEphemeral,
                SelectedPrivilegeLevel = createDlg.SelectedPrivilegeLevel,
                FirewallSettingsChanged = createDlg.FirewallSettingsChanged,
                AllowInternet = createDlg.AllowInternet,
                AllowLocalhost = createDlg.AllowLocalhost,
                AllowLan = createDlg.AllowLan,
                SettingsImportPath = createDlg.SettingsImportPath
            };

            var persistenceResult = await persistenceCoordinator.PersistOrRollbackAsync(
                persistenceRequest,
                session.CredentialStore,
                session.Database);

            switch (persistenceResult.Status)
            {
                case RunAsCreatedAccountPersistenceStatus.CleanupStateSaveFailed:
                    errorPresenter.ShowCleanupStateSaveFailed(persistenceResult.ErrorMessage);
                    return null;
                case RunAsCreatedAccountPersistenceStatus.CredentialSaveRolledBack:
                    errorPresenter.ShowCredentialSaveRolledBack(
                        persistenceResult.ErrorMessage
                        ?? throw new InvalidOperationException("Missing save error for rolled-back credential persistence."));
                    return null;
                case RunAsCreatedAccountPersistenceStatus.CredentialSaveRollbackFailed:
                    errorPresenter.ShowCredentialSaveRollbackFailed(
                        persistenceResult.ErrorMessage
                        ?? throw new InvalidOperationException("Missing save error for failed credential rollback."),
                        persistenceResult.RollbackErrorMessage
                        ?? throw new InvalidOperationException("Missing rollback error for failed credential rollback."));
                    return null;
                case RunAsCreatedAccountPersistenceStatus.PrePersistenceRolledBack:
                    errorPresenter.ShowPrePersistenceRolledBack(
                        persistenceResult.ErrorMessage
                        ?? throw new InvalidOperationException("Missing pre-persistence error for rolled-back account creation."));
                    return null;
                case RunAsCreatedAccountPersistenceStatus.PrePersistenceRollbackFailed:
                    errorPresenter.ShowPrePersistenceRollbackFailed(
                        persistenceResult.ErrorMessage
                        ?? throw new InvalidOperationException("Missing pre-persistence error for failed account rollback."),
                        persistenceResult.RollbackErrorMessage
                        ?? throw new InvalidOperationException("Missing rollback error for failed pre-persistence rollback."));
                    return null;
                case RunAsCreatedAccountPersistenceStatus.Succeeded:
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected persistence status: {persistenceResult.Status}.");
            }

            var credential = persistenceResult.Credential
                ?? throw new InvalidOperationException("Missing credential for successful RunAs account creation.");
            var postSetupResult = await postSetupService.CompleteAsync(postSetupRequest, credential);
            if (postSetupResult.WasCanceled)
            {
                dataChangeNotifier.NotifyDataChanged();
                return null;
            }

            errorPresenter.ShowPostSetupWarnings(postSetupResult.WarningMessages);
            dataChangeNotifier.NotifyDataChanged();
            return new RunAsCreatedAccountResult(credential, postSetupResult.PermissionGrant);
        }
        finally
        {
            createDlg.CreatedPassword?.Dispose();
            createDlg.Dispose();
            modalCoordinator.EndModal();
        }
    }
}

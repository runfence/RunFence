using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.RunAs;

public sealed class RunAsCreatedAccountPersistenceCoordinator(
    RunAsCredentialCreator credentialCreator,
    CreatedAccountRollbackExecutor rollbackExecutor,
    IDataChangeNotifier dataChangeNotifier,
    ILoggingService log)
{
    public async Task<RunAsCreatedAccountPersistenceResult> PersistOrRollbackAsync(
        RunAsCreatedAccountPersistenceRequest request,
        CredentialStore credentialStore,
        AppDatabase database)
    {
        if (request.CreatedAccountStatus == CreateAccountStatus.CleanupStateSaveFailed)
        {
            dataChangeNotifier.NotifyDataChanged();
            return new RunAsCreatedAccountPersistenceResult(
                RunAsCreatedAccountPersistenceStatus.CleanupStateSaveFailed,
                null,
                DataChangeNotified: true,
                ErrorMessage: request.CreatedAccountErrorMessage);
        }

        try
        {
            if (string.IsNullOrEmpty(request.CreatedSid))
                throw new InvalidOperationException("Missing SID for created RunAs account.");

            if (string.IsNullOrEmpty(request.Username))
                throw new InvalidOperationException("Missing username for created RunAs account.");

            if (request.CreatedPassword == null)
                throw new InvalidOperationException("Missing password for created RunAs account.");

            if (request.CreatedRollbackState == null)
                throw new InvalidOperationException("Missing rollback state for created RunAs account.");

            var credential = credentialCreator.PersistCredential(
                request.CreatedPassword,
                request.CreatedSid,
                request.Username,
                request.CreatedRollbackState);
            return new RunAsCreatedAccountPersistenceResult(
                RunAsCreatedAccountPersistenceStatus.Succeeded,
                credential,
                DataChangeNotified: false);
        }
        catch (RunAsCredentialPersistenceException saveEx)
        {
            return await RollbackAfterFailureAsync(
                request,
                credentialStore,
                database,
                saveEx.RollbackState,
                saveEx.SaveException.Message,
                RunAsCreatedAccountPersistenceStatus.CredentialSaveRolledBack,
                RunAsCreatedAccountPersistenceStatus.CredentialSaveRollbackFailed,
                "RunAsCreatedAccountPersistenceCoordinator: rollback failed after credential save failure");
        }
        catch (Exception saveEx)
        {
            if (request.CreatedRollbackState == null)
            {
                log.Error(
                    "RunAsCreatedAccountPersistenceCoordinator: missing rollback state after pre-persistence failure; scheduling ephemeral cleanup",
                    saveEx);
                ScheduleEphemeralCleanupIfRequested(request, database);
                throw;
            }

            return await RollbackAfterFailureAsync(
                request,
                credentialStore,
                database,
                request.CreatedRollbackState,
                saveEx.Message,
                RunAsCreatedAccountPersistenceStatus.PrePersistenceRolledBack,
                RunAsCreatedAccountPersistenceStatus.PrePersistenceRollbackFailed,
                "RunAsCreatedAccountPersistenceCoordinator: rollback failed after pre-persistence error");
        }
    }

    private async Task<RunAsCreatedAccountPersistenceResult> RollbackAfterFailureAsync(
        RunAsCreatedAccountPersistenceRequest request,
        CredentialStore credentialStore,
        AppDatabase database,
        CreatedAccountRollbackState rollbackState,
        string errorMessage,
        RunAsCreatedAccountPersistenceStatus rollbackSucceededStatus,
        RunAsCreatedAccountPersistenceStatus rollbackFailedStatus,
        string rollbackFailureLogMessage)
    {
        try
        {
            await rollbackExecutor.RollbackAsync(rollbackState, credentialStore, database);
            return new RunAsCreatedAccountPersistenceResult(
                rollbackSucceededStatus,
                null,
                DataChangeNotified: false,
                ErrorMessage: errorMessage);
        }
        catch (Exception rollbackEx)
        {
            log.Error(rollbackFailureLogMessage, rollbackEx);
            var notified = ScheduleEphemeralCleanupIfRequested(request, database);
            return new RunAsCreatedAccountPersistenceResult(
                rollbackFailedStatus,
                null,
                DataChangeNotified: notified,
                ErrorMessage: errorMessage,
                RollbackErrorMessage: rollbackEx.Message);
        }
    }

    private bool ScheduleEphemeralCleanupIfRequested(
        RunAsCreatedAccountPersistenceRequest request,
        AppDatabase database)
    {
        if (!request.ScheduleEphemeralCleanupOnRollbackFailure)
            return false;

        if (string.IsNullOrEmpty(request.CreatedSid))
            throw new InvalidOperationException("Missing SID for created RunAs account cleanup.");

        database.GetOrCreateAccount(request.CreatedSid).DeleteAfterUtc = DateTime.UtcNow.AddHours(1);
        dataChangeNotifier.NotifyDataChanged();
        return true;
    }
}

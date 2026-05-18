using RunFence.Account.UI;

namespace RunFence.RunAs;

public sealed class RunAsAccountCreationErrorPresenter(
    IAccountMessageBoxService messageBoxService)
{
    public void ShowCleanupStateSaveFailed(string? errorMessage)
    {
        messageBoxService.Show(
            owner: null,
            "Windows created the account, but RunFence could not save its cleanup state.\n\n" +
            $"The account remains in memory for this session only:\n{errorMessage}",
            "Account Created But Not Saved",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public void ShowCredentialSaveRolledBack(string errorMessage)
    {
        messageBoxService.Show(
            owner: null,
            "Windows created the account, but RunFence could not save the credential store.\n\n" +
            "The account was rolled back:\n" +
            errorMessage,
            "Account Creation Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void ShowCredentialSaveRollbackFailed(string errorMessage, string rollbackErrorMessage)
    {
        messageBoxService.Show(
            owner: null,
            "Windows created the account, but RunFence could not save the credential store and rollback also failed.\n\n" +
            $"Save error: {errorMessage}\n" +
            $"Rollback error: {rollbackErrorMessage}",
            "Account Creation Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void ShowPrePersistenceRolledBack(string errorMessage)
    {
        messageBoxService.Show(
            owner: null,
            "Windows created the account, but RunFence failed before credential persistence completed.\n\n" +
            "The account was rolled back:\n" +
            errorMessage,
            "Account Creation Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void ShowPrePersistenceRollbackFailed(string errorMessage, string rollbackErrorMessage)
    {
        messageBoxService.Show(
            owner: null,
            "Windows created the account, but RunFence failed before credential persistence completed and rollback also failed.\n\n" +
            $"Error: {errorMessage}\n" +
            $"Rollback error: {rollbackErrorMessage}",
            "Account Creation Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void ShowPostSetupWarnings(IReadOnlyList<string> warningMessages)
    {
        if (warningMessages.Count == 0)
            return;

        messageBoxService.Show(
            owner: null,
            $"Account created with warnings:\n\n{string.Join("\n", warningMessages)}",
            "RunFence",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}

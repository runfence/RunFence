namespace RunFence.Startup.UI;

public interface IAccountConfigTransferPromptService
{
    bool ConfirmOverwriteExistingData(string targetAccountSid);
    void ShowMigrationFailed(string targetAccountSid, Exception error);
    bool ConfirmDeleteCurrentData(string targetAccountSid);
    void ShowCleanupFailed(string targetAccountSid, Exception error);
}

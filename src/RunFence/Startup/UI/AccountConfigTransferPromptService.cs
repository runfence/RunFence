using RunFence.Account;
using RunFence.Infrastructure;

namespace RunFence.Startup.UI;

public class AccountConfigTransferPromptService(
    ISidNameCacheService sidNameCache) : IAccountConfigTransferPromptService
{
    public bool ConfirmOverwriteExistingData(string targetAccountSid)
    {
        var targetDisplayName = sidNameCache.GetDisplayName(targetAccountSid);
        if (string.IsNullOrWhiteSpace(targetDisplayName))
            targetDisplayName = targetAccountSid;
        return MessageBox.Show(
                   $"{targetDisplayName} already has RunFence data. Replace it?",
                   "Overwrite Existing Data?",
                   MessageBoxButtons.YesNo,
                   MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    public void ShowMigrationFailed(string targetAccountSid, Exception error)
    {
        MessageBox.Show(
            $"Migration failed: {error.Message}",
            "Migration Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public bool ConfirmDeleteCurrentData(string targetAccountSid)
    {
        var targetDisplayName = sidNameCache.GetDisplayName(targetAccountSid);
        if (string.IsNullOrWhiteSpace(targetDisplayName))
            targetDisplayName = targetAccountSid;
        return MessageBox.Show(
                   $"Migration to {targetDisplayName} complete.\n\nDelete current account's RunFence data (config, credentials, license) and exit?",
                   "Migration Complete",
                   MessageBoxButtons.YesNo,
                   MessageBoxIcon.Question) == DialogResult.Yes;
    }

    public void ShowCleanupFailed(string targetAccountSid, Exception error)
    {
        MessageBox.Show(
            $"Migration succeeded but could not delete current data: {error.Message}\n\nYou may delete the files manually.",
            "Cleanup Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}

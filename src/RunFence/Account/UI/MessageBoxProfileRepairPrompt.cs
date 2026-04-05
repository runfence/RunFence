namespace RunFence.Account.UI;

/// <summary>
/// Default <see cref="IProfileRepairPrompt"/> implementation that shows standard
/// MessageBox dialogs for profile repair interaction.
/// </summary>
public sealed class MessageBoxProfileRepairPrompt : IProfileRepairPrompt
{
    public bool ConfirmRepair(string accountName) =>
        MessageBox.Show(
            $"Windows corrupted the profile for '{accountName}'.\n\n" +
            "Close all applications running under that account before continuing.\n\n" +
            "Would you like to repair the profile?",
            "Profile Corruption Detected",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    public void NotifyRepairFailed() =>
        MessageBox.Show(
            "Failed to repair the profile. Check the log for details.",
            "Repair Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public bool ConfirmRetry() =>
        MessageBox.Show(
            "Profile repaired successfully. Retry the launch?",
            "Profile Repaired",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
}
namespace RunFence.UI;

/// <summary>
/// Production implementation of <see cref="IStartWithoutPinPromptService"/> that shows
/// <see cref="MessageBox"/> dialogs.
/// </summary>
public class StartWithoutPinPromptService : IStartWithoutPinPromptService
{
    public bool ConfirmSecurityWarning()
    {
        var result = MessageBox.Show(
            "This feature stores your PIN-derived encryption key protected by your Windows account password.\n" +
            "The key is rotated when you enable or disable this feature,\n" +
            "To use this feature securely, create a separate Admin account with a strong password and migrate your config there.\n" +
            "It doesn't matter if you use same password for another Windows account as long as the password is not compromised.\n" +
            "For enhanced security, we will also try to use TPM if available.",
            "Security Warning",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        return result == DialogResult.OK;
    }

    public bool ConfirmDpapiOnlyWarning()
    {
        var result = MessageBox.Show(
            "TPM is not available on this machine. The key will be stored with software-only " +
            "protection (DPAPI), which provides less security against advanced attacks. Continue?",
            "TPM Not Available",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        return result == DialogResult.OK;
    }

    public void ShowTpmFallbackWarning()
    {
        MessageBox.Show(
            "TPM is present but does not support the required encryption operations on this system. " +
            "The key will be stored with software-only protection (DPAPI).",
            "TPM Not Functional",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

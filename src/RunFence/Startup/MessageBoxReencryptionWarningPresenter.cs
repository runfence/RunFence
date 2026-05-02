namespace RunFence.Startup;

public class MessageBoxReencryptionWarningPresenter : IReencryptionWarningPresenter
{
    public void ShowWarning(string message) =>
        MessageBox.Show(message, "Re-encryption Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

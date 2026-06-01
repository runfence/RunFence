using System.Windows.Forms;

namespace RunFence.RunAs.UI;

public sealed class RunAsAdHocPasswordPromptService(IRunAsPasswordDialogAdapterFactory dialogFactory) : IRunAsAdHocPasswordPromptService
{
    public RunAsPasswordPromptResult Prompt(
        IWin32Window? owner,
        string accountSid,
        string usernameFallback,
        string accountDisplayName,
        bool allowRememberPassword)
    {
        using var dialog = dialogFactory.Create(
            accountDisplayName,
            allowRememberPassword,
            accountSid,
            usernameFallback);
        var accepted = dialog.ShowDialog(owner) == DialogResult.OK;
        return new RunAsPasswordPromptResult(
            accepted,
            accepted ? dialog.Password : null,
            accepted && allowRememberPassword && dialog.RememberPassword);
    }
}

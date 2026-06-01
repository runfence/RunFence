using System.Windows.Forms;

namespace RunFence.RunAs.UI;

public interface IRunAsAdHocPasswordPromptService
{
    RunAsPasswordPromptResult Prompt(
        IWin32Window? owner,
        string accountSid,
        string usernameFallback,
        string accountDisplayName,
        bool allowRememberPassword);
}

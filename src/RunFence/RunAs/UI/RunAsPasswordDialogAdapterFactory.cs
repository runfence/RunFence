using RunFence.Account;
using RunFence.RunAs.UI.Forms;

namespace RunFence.RunAs.UI;

public sealed class RunAsPasswordDialogAdapterFactory(IAccountPasswordService accountPasswordService) : IRunAsPasswordDialogAdapterFactory
{
    public IRunAsPasswordDialogAdapter Create(
        string accountDisplayName,
        bool allowRememberPassword,
        string accountSid,
        string usernameFallback)
    {
        return new RunAsPasswordDialogAdapter(
            new RunAsPasswordDialog(
                accountDisplayName,
                allowRememberPassword,
                accountPasswordService,
                accountSid,
                usernameFallback));
    }
}

using RunFence.Security.UI.Forms;

namespace RunFence.Security;

public class InputInjectionDisableBlockingDialogService : IInputInjectionDisableBlockingDialogService
{
    public DisableBlockingChoice Show()
    {
        using var dialog = new DisableBlockingDialog();
        dialog.ShowDialog();
        return dialog.Choice;
    }
}

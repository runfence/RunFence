namespace RunFence.Account.UI;

public interface IAccountMessageBoxService
{
    DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1);
}

public class AccountMessageBoxService : IAccountMessageBoxService
{
    public DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        => owner != null
            ? MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
            : MessageBox.Show(text, caption, buttons, icon, defaultButton);
}

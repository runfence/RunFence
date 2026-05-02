namespace RunFence.Apps.UI;

public class MessageBoxService : IMessageBoxService
{
    public DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => MessageBox.Show(text, caption, buttons, icon);
}

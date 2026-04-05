using System.Security;
using RunFence.Apps.UI;

namespace RunFence.Account.UI.Forms;

public partial class PasswordInputDialog : Form
{
    public SecureString? Password { get; private set; }

    public PasswordInputDialog(string username)
    {
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _promptLabel.Text = $"Enter current Windows password for \u201C{username}\u201D:";
        PasswordEyeToggle.AddTo(_passwordTextBox);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        Password = new SecureString();
        foreach (char c in _passwordTextBox.Text)
            Password.AppendChar(c);
        Password.MakeReadOnly();
        DialogResult = DialogResult.OK;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
    }
}
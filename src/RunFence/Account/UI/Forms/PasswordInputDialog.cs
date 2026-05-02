using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;

namespace RunFence.Account.UI.Forms;

public partial class PasswordInputDialog : Form
{
    private readonly SecurePasswordBox _passwordSecure;

    public ProtectedString? Password { get; private set; }

    public PasswordInputDialog(string username, string? prompt = null)
    {
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _promptLabel.Text = prompt ?? $"Enter current Windows password for \u201C{username}\u201D:";
        _passwordSecure = new SecurePasswordBox(_passwordTextBox);
        _passwordSecure.AddEyeToggle();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        Password = _passwordSecure.GetPassword();
        DialogResult = DialogResult.OK;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
    }
}
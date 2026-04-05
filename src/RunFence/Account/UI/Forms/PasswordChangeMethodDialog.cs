using System.Security;
using RunFence.Apps.UI;

namespace RunFence.Account.UI.Forms;

public partial class PasswordChangeMethodDialog : Form
{
    public SecureString? EnteredPassword { get; private set; }
    public bool ForceResetRequested { get; private set; }

    public PasswordChangeMethodDialog(bool isCurrentAccount)
    {
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _changeButton.Enabled = false;
        _forceResetButton.Enabled = !isCurrentAccount;
        PasswordEyeToggle.AddTo(_passwordTextBox);
    }

    private void OnPasswordTextChanged(object? sender, EventArgs e)
        => _changeButton.Enabled = _passwordTextBox.Text.Length > 0;

    private void OnChangeClick(object? sender, EventArgs e)
    {
        EnteredPassword = new SecureString();
        foreach (char c in _passwordTextBox.Text)
            EnteredPassword.AppendChar(c);
        EnteredPassword.MakeReadOnly();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnForceResetClick(object? sender, EventArgs e)
    {
        ForceResetRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
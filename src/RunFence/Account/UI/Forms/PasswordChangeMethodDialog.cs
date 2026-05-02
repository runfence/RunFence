using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;

namespace RunFence.Account.UI.Forms;

public partial class PasswordChangeMethodDialog : Form
{
    private readonly SecurePasswordBox _passwordSecure;

    public ProtectedString? EnteredPassword { get; private set; }
    public bool ForceResetRequested { get; private set; }

    public PasswordChangeMethodDialog(bool isCurrentAccount)
    {
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _changeButton.Enabled = false;
        _forceResetButton.Enabled = !isCurrentAccount;
        _passwordSecure = new SecurePasswordBox(_passwordTextBox);
        _passwordSecure.AddEyeToggle();
    }

    private void OnPasswordTextChanged(object? sender, EventArgs e)
        => _changeButton.Enabled = !_passwordSecure.IsEmpty;

    private void OnChangeClick(object? sender, EventArgs e)
    {
        EnteredPassword = _passwordSecure.GetPassword();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnForceResetClick(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Force resetting permanently destroys the account's EFS encrypted files and Windows Credential Manager entries.\n\nContinue?",
            "Force Reset Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
            return;
        ForceResetRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
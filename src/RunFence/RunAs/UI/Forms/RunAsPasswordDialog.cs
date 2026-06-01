using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;

namespace RunFence.RunAs.UI.Forms;

public partial class RunAsPasswordDialog : RunFence.UI.Forms.ContextHelpForm
{
    private readonly IAccountPasswordService _accountService;
    private readonly string _sid;
    private readonly string _usernameFallback;
    private readonly SecurePasswordBox _passwordSecure;

    public ProtectedString? Password { get; private set; }
    public bool RememberPassword => _rememberCheckBox.Checked;

    public RunAsPasswordDialog(string accountDisplayName, bool allowRememberPassword, IAccountPasswordService accountService,
        string sid, string usernameFallback)
    {
        _accountService = accountService;
        _sid = sid;
        _usernameFallback = usernameFallback;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _accountLabel.Text = $"Enter password for: {accountDisplayName}";
        _passwordSecure = new SecurePasswordBox(_passwordTextBox);
        _passwordSecure.AddEyeToggle();
        _rememberCheckBox.Visible = allowRememberPassword;
        _rememberCheckBox.Enabled = allowRememberPassword;
        if (!allowRememberPassword)
            _rememberCheckBox.Checked = false;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = "";

        if (_passwordSecure.IsEmpty)
        {
            _statusLabel.Text = "Password is required.";
            return;
        }

        var pwd = _passwordSecure.GetPassword();

        var result = _accountService.ValidatePassword(_sid, pwd, _usernameFallback);
        if (result.Status != AccountPasswordStatus.Succeeded)
        {
            pwd.Dispose();
            _statusLabel.Text = result.Error is string errorText
                ? errorText
                : "Credential validation failed.";
            return;
        }

        Password = pwd;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}

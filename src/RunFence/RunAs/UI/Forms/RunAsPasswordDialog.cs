using System.Security;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.UI;

namespace RunFence.RunAs.UI.Forms;

public partial class RunAsPasswordDialog : Form
{
    private readonly IWindowsAccountService _accountService;
    private readonly string _sid;
    private readonly string _usernameFallback;

    public SecureString? Password { get; private set; }
    public bool RememberPassword => _rememberCheckBox.Checked;

    public RunAsPasswordDialog(string accountDisplayName, IWindowsAccountService accountService,
        string sid, string usernameFallback)
    {
        _accountService = accountService;
        _sid = sid;
        _usernameFallback = usernameFallback;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _accountLabel.Text = $"Enter password for: {accountDisplayName}";
        PasswordEyeToggle.AddTo(_passwordTextBox);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _statusLabel.Text = "";

        if (_passwordTextBox.Text.Length == 0)
        {
            _statusLabel.Text = "Password is required.";
            return;
        }

        var error = _accountService.ValidatePassword(_sid, _passwordTextBox.Text, _usernameFallback);
        if (error != null)
        {
            _statusLabel.Text = error;
            return;
        }

        Password = new SecureString();
        foreach (char c in _passwordTextBox.Text)
            Password.AppendChar(c);
        Password.MakeReadOnly();

        _passwordTextBox.Clear();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.RunAs.UI.Forms;

public partial class CallerIdentityDialog : Form
{
    public string? Result { get; private set; }
    public string? ResolvedName { get; private set; }

    private readonly List<LocalUserAccount> _localUsers;
    private readonly ISidEntryHelper _sidEntryHelper;

    public CallerIdentityDialog(List<LocalUserAccount> localUsers, ISidEntryHelper sidEntryHelper)
    {
        _localUsers = localUsers;
        _sidEntryHelper = sidEntryHelper;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        foreach (var user in _localUsers)
            _identityComboBox.Items.Add(user.Username);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var trimmed = _identityComboBox.Text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _statusLabel.Text = "Identity cannot be empty.";
            return;
        }

        var sid = _sidEntryHelper.ResolveOrPrompt(trimmed, _localUsers, this);
        if (sid != null)
        {
            Result = sid;
            ResolvedName = trimmed;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
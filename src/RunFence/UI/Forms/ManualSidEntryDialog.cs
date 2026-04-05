using System.Security.Principal;
using RunFence.Apps.UI;

namespace RunFence.UI.Forms;

public partial class ManualSidEntryDialog : Form
{
    public string? ResultSid { get; private set; }

    public ManualSidEntryDialog(string accountName)
    {
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _infoLabel.Text = $"Could not resolve '{accountName}' to a SID.\nEnter the SID manually (e.g., S-1-5-21-...):";
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var input = _sidTextBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            _errorLabel.Text = "SID cannot be empty.";
            return;
        }

        try
        {
            _ = new SecurityIdentifier(input);
            ResultSid = input;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch
        {
            _errorLabel.Text = "Invalid SID format.";
        }
    }
}
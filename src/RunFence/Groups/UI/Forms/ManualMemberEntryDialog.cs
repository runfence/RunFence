namespace RunFence.Groups.UI.Forms;

internal partial class ManualMemberEntryDialog : Form
{
    public string? EnteredValue { get; private set; }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var input = _inputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            _errorLabel.Text = "Please enter a username or SID.";
            return;
        }

        EnteredValue = input;
        DialogResult = DialogResult.OK;
        Close();
    }
}

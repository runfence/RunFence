namespace RunFence.UI.Forms;

/// <summary>
/// Simple single-line text input prompt dialog.
/// </summary>
public partial class InputPromptDialog : Form
{
    public string? Value { get; private set; }

    public InputPromptDialog(string title, string prompt)
    {
        InitializeComponent();
        Text = title;
        _promptLabel.Text = prompt;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        Value = _inputTextBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
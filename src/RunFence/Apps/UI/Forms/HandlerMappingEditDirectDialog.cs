namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dialog for editing the handler value of an existing direct handler mapping.
/// Call <see cref="Initialize"/> before <see cref="Form.ShowDialog()"/>.
/// Read <see cref="NewValue"/> after the dialog is accepted.
/// </summary>
public partial class HandlerMappingEditDirectDialog : Form
{
    private string _currentValue = string.Empty;

    /// <summary>
    /// The trimmed new handler value entered by the user, or null if unchanged/empty.
    /// Set only when the dialog is accepted with a non-empty value different from the original.
    /// </summary>
    public string? NewValue { get; private set; }

    /// <summary>
    /// Initializes per-use dialog data. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(string key, string currentValue)
    {
        _currentValue = currentValue;
        Text = $"Edit Direct Handler — {key}";
        _label.Text = $"Handler for \"{key}\" (class name or command):";
        _textBox.Text = currentValue;
        UpdateOkButtonState();
    }

    private void UpdateOkButtonState()
    {
        _okButton.Enabled = !string.IsNullOrWhiteSpace(_textBox.Text);
    }

    private void OnTextBoxTextChanged(object? sender, EventArgs e) => UpdateOkButtonState();

    private void OnOkClick(object? sender, EventArgs e)
    {
        var newValue = _textBox.Text.Trim();
        if (!string.IsNullOrEmpty(newValue) &&
            !string.Equals(newValue, _currentValue, StringComparison.OrdinalIgnoreCase))
        {
            NewValue = newValue;
        }
    }
}

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Small modal dialog that prompts the user to enter or select an extension/protocol key
/// for a handler association. Returns the trimmed, lower-cased key via <see cref="SelectedKey"/>.
/// </summary>
public class AssociationKeyInputDialog : Form
{
    private readonly ComboBox _keyCombo;

    /// <summary>The validated, trimmed, lower-cased key entered by the user. Null until OK is clicked.</summary>
    public string? SelectedKey { get; private set; }

    /// <param name="title">Dialog title (e.g. "Add Association").</param>
    /// <param name="suggestions">Items pre-populated in the combo drop-down.</param>
    public AssociationKeyInputDialog(string title, IEnumerable<string> suggestions)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(300, 100);

        _keyCombo = new ComboBox
        {
            Location = new Point(15, 15),
            Size = new Size(270, 23),
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _keyCombo.Items.AddRange(suggestions.Cast<object>().ToArray());

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(130, 55),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.System
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(210, 55),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.System
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(_keyCombo, okButton, cancelButton);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult == DialogResult.OK)
            SelectedKey = _keyCombo.Text.Trim().ToLowerInvariant();
    }
}
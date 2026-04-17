using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Small modal dialog that prompts the user to enter or select an extension/protocol key
/// for a handler association, along with the arguments template for that association.
/// Returns the trimmed, lower-cased key via <see cref="SelectedKey"/> and template via <see cref="SelectedTemplate"/>.
/// </summary>
public class AssociationKeyInputDialog : Form
{
    private readonly ComboBox _keyCombo;
    private readonly TextBox _templateTextBox;

    /// <summary>The validated, trimmed, lower-cased key entered by the user. Null until OK is clicked.</summary>
    public string? SelectedKey { get; private set; }

    /// <summary>The arguments template entered by the user, or null if blank. Null until OK is clicked.</summary>
    public string? SelectedTemplate { get; private set; }

    /// <summary>
    /// When set, called with the current key to look up a registry-suggested arguments template.
    /// Return value replaces the template field content; return null to reset to the default <c>"%1"</c>.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, string?>? TemplateLoader { get; set; }

    /// <param name="title">Dialog title (e.g. "Add Association").</param>
    /// <param name="suggestions">Items pre-populated in the combo drop-down.</param>
    public AssociationKeyInputDialog(string title, IEnumerable<string> suggestions)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(300, 145);

        _keyCombo = new ComboBox
        {
            Location = new Point(15, 15),
            Size = new Size(270, 23),
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _keyCombo.Items.AddRange(suggestions.Cast<object>().ToArray());
        _keyCombo.TextChanged += OnKeyChanged;
        _keyCombo.SelectedIndexChanged += OnKeyChanged;

        var templateLabel = new Label
        {
            Text = "Parameters Template:",
            Location = new Point(15, 48),
            AutoSize = true
        };

        _templateTextBox = new TextBox
        {
            Location = new Point(15, 65),
            Size = new Size(270, 23),
            Text = "\"%1\""
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(130, 105),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.System
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(210, 105),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.System
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(_keyCombo, templateLabel, _templateTextBox, okButton, cancelButton);
    }

    private void OnKeyChanged(object? sender, EventArgs e)
    {
        if (TemplateLoader == null)
            return;
        var key = _keyCombo.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return;
        _templateTextBox.Text = TemplateLoader(key) ?? "\"%1\"";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult == DialogResult.OK)
        {
            SelectedKey = _keyCombo.Text.Trim().ToLowerInvariant();
            SelectedTemplate = string.IsNullOrWhiteSpace(_templateTextBox.Text) ? null : _templateTextBox.Text.Trim();
        }
    }
}

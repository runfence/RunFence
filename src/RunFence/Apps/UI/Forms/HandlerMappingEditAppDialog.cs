using RunFence.Core.Models;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Small dialog for changing the target app and arguments template of an existing handler mapping.
/// Call <see cref="Initialize"/> before <see cref="Form.ShowDialog()"/>.
/// After acceptance, read the result via <see cref="SelectedApp"/> and <see cref="NewTemplate"/>.
/// </summary>
public class HandlerMappingEditAppDialog : Form
{
    private readonly IExeAssociationRegistryReader _reader;
    private Label _appLabel = null!;
    private ComboBox _appCombo = null!;
    private Label _templateLabel = null!;
    private TextBox _templateTextBox = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;
    private string _key = string.Empty;

    /// <summary>The selected application after the dialog is accepted, or null if none was selected.</summary>
    public AppEntry? SelectedApp { get; private set; }

    /// <summary>The trimmed arguments template after the dialog is accepted, or null if blank.</summary>
    public string? NewTemplate { get; private set; }

    public HandlerMappingEditAppDialog(IExeAssociationRegistryReader reader)
    {
        _reader = reader;
        BuildControls();
    }

    /// <summary>
    /// Populates the dialog with the available apps and the currently selected app/template.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(string key, IReadOnlyList<AppEntry> apps, AppEntry? currentApp, string? currentTemplate)
    {
        _key = key;
        Text = $"Edit Association — {key}";
        _appLabel.Text = $"Application for \"{key}\":";

        foreach (var app in apps.Where(a => a is { IsFolder: false, IsUrlScheme: false }).OrderBy(a => a.Name))
            _appCombo.Items.Add(new AppComboItem(app));

        for (var i = 0; i < _appCombo.Items.Count; i++)
        {
            if (_appCombo.Items[i] is AppComboItem item &&
                string.Equals(item.App.Id, currentApp?.Id, StringComparison.OrdinalIgnoreCase))
            {
                _appCombo.SelectedIndex = i;
                break;
            }
        }

        if (_appCombo is { SelectedIndex: < 0, Items.Count: > 0 })
            _appCombo.SelectedIndex = 0;

        _templateTextBox.Text = currentTemplate ?? string.Empty;
        _appCombo.SelectedIndexChanged += OnAppComboSelectedIndexChanged;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult != DialogResult.OK)
            return;

        if (_appCombo.SelectedItem is not AppComboItem selected)
        {
            e.Cancel = true;
            return;
        }

        SelectedApp = selected.App;
        NewTemplate = string.IsNullOrWhiteSpace(_templateTextBox.Text) ? null : _templateTextBox.Text.Trim();
    }

    private void OnAppComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        var exePath = (_appCombo.SelectedItem as AppComboItem)?.App.ExePath;
        if (string.IsNullOrEmpty(exePath))
            return;
        var args = _reader.GetNonDefaultArguments(exePath, _key);
        _templateTextBox.Text = args ?? "\"%1\"";
    }

    private void BuildControls()
    {
        Text = "Edit Association";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(350, 150);

        _appLabel = new Label { Text = "Application:", Location = new Point(15, 12), AutoSize = true };

        _appCombo = new ComboBox
        {
            Location = new Point(15, 32),
            Size = new Size(320, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        _templateLabel = new Label { Text = "Parameters Template:", Location = new Point(15, 65), AutoSize = true };

        _templateTextBox = new TextBox
        {
            Location = new Point(15, 82),
            Size = new Size(320, 23)
        };

        _okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(175, 115),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.System
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(260, 115),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.System
        };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _appLabel, _appCombo, _templateLabel, _templateTextBox, _okButton, _cancelButton });
    }
}

using Timer = System.Windows.Forms.Timer;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Edit dialog for a single handler association entry within AppEditDialog.
/// Serves both Add mode (user picks the key, template, and prefixes) and Edit mode
/// (key is fixed; user edits template and prefix overrides only).
/// </summary>
public class HandlerAssociationEditDialog : Form
{
    private Label _keyLabel = null!;
    private ComboBox _keyCombo = null!;
    private Label _templateLabel = null!;
    private TextBox _templateTextBox = null!;
    private CombinedPrefixesSection _combinedPrefixesSection = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;

    private HandlerAssociationDialogValueHelper? _valueHelper;
    private string _exePath = "";
    private bool _addMode;
    private Timer? _debounceTimer;

    /// <summary>The validated, trimmed, lower-cased key. Populated after OK in Add mode; null in Edit mode.</summary>
    public string? SelectedKey { get; private set; }

    public string? NewTemplate { get; private set; }
    public IReadOnlyList<string>? NewPrefixes { get; private set; }
    public bool NewReplacePrefixes { get; private set; }

    public HandlerAssociationEditDialog()
    {
        BuildControls();
    }

    private void BuildControls()
    {
        _keyLabel = new Label();
        _keyCombo = new ComboBox();
        _templateLabel = new Label();
        _templateTextBox = new TextBox();
        _combinedPrefixesSection = new CombinedPrefixesSection();
        _combinedPrefixesSection.EnableFlatMode();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // Key label — shown in both modes; in Edit mode carries the key text
        _keyLabel.Text = "Association:";
        _keyLabel.Location = new Point(15, 12);
        _keyLabel.AutoSize = true;

        // Key combo — visible in Add mode only (hidden in Edit mode)
        _keyCombo.Location = new Point(15, 32);
        _keyCombo.Size = new Size(340, 23);
        _keyCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _keyCombo.TextChanged += OnKeyTextChanged;
        _keyCombo.SelectedIndexChanged += OnKeySelectedIndexChanged;

        // Template section — in Add mode at y=67/87; in Edit mode shifted up to y=35/55
        _templateLabel.Text = "Parameters Template:";
        _templateLabel.Location = new Point(15, 67);
        _templateLabel.AutoSize = true;

        _templateTextBox.Location = new Point(15, 87);
        _templateTextBox.Size = new Size(340, 23);

        // CombinedPrefixesSection — in Add mode at y=120, height=276; in Edit mode at y=88, height=308
        _combinedPrefixesSection.Dock = DockStyle.None;
        _combinedPrefixesSection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _combinedPrefixesSection.Location = new Point(15, 120);
        _combinedPrefixesSection.Size = new Size(340, 276);

        _okButton.Text = "OK";
        _okButton.Location = new Point(200, 404);
        _okButton.Size = new Size(75, 28);
        _okButton.DialogResult = DialogResult.OK;
        _okButton.FlatStyle = FlatStyle.System;

        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(280, 404);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.FlatStyle = FlatStyle.System;

        Text = "Edit Association";
        Icon = AppIcons.GetAppIcon();
        ClientSize = new Size(370, 440);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.AddRange(_keyLabel, _keyCombo, _templateLabel, _templateTextBox,
            _combinedPrefixesSection, _okButton, _cancelButton);

        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// Initializes the dialog for Add mode. The user selects a key from <paramref name="suggestions"/>,
    /// and the template is auto-filled from the registry when a key is chosen.
    /// </summary>
    public void InitializeForAdd(
        IEnumerable<string> suggestions,
        IExeAssociationRegistryReader reader,
        string exePath)
    {
        _addMode = true;
        _valueHelper = new HandlerAssociationDialogValueHelper(reader);
        _exePath = exePath;

        Text = "Add Association";
        _keyLabel.Text = "Association:";
        _keyCombo.Visible = true;
        _keyCombo.Items.AddRange(suggestions.Cast<object>().ToArray());
        _templateTextBox.Text = HandlerAssociationDialogValueHelper.DefaultArgumentsTemplate;

        _combinedPrefixesSection.SetAssociationPrefixes(null, false);
    }

    /// <summary>
    /// Initializes the dialog for Edit mode. The key is fixed and shown in the label.
    /// In Edit mode the key combo is hidden, giving the prefix section ~32px more height.
    /// </summary>
    public void Initialize(
        string key,
        string? currentTemplate,
        IReadOnlyList<string>? currentAssocPrefixes,
        bool currentReplacePrefixes)
    {
        _addMode = false;

        Text = $"Edit Association \u2014 {key}";
        _keyLabel.Text = $"Association \u2014 {key}";
        _keyCombo.Visible = false;

        // Shift template section up since the key combo row is hidden
        _templateLabel.Location = new Point(15, 35);
        _templateTextBox.Location = new Point(15, 55);
        _combinedPrefixesSection.Location = new Point(15, 88);
        _combinedPrefixesSection.Size = new Size(340, 308);

        _templateTextBox.Text = currentTemplate ?? "";
        _combinedPrefixesSection.SetAssociationPrefixes(currentAssocPrefixes, currentReplacePrefixes);
    }

    private void OnKeyTextChanged(object? sender, EventArgs e)
    {
        if (_valueHelper == null)
            return;
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer { Interval = 300 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer!.Stop();
            _debounceTimer.Dispose();
            _debounceTimer = null;
            LoadTemplate();
        };
        _debounceTimer.Start();
    }

    private void OnKeySelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_valueHelper == null)
            return;
        // Cancel any pending debounce and load immediately. TextChanged may still fire after
        // this returns (WinForms updates combo text after SelectedIndexChanged), but the 300ms
        // debounce it starts is harmless — it will load the same key again.
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        LoadTemplate();
    }

    private void LoadTemplate()
    {
        if (_valueHelper == null)
            return;

        var key = HandlerAssociationDialogValueHelper.NormalizeKey(_keyCombo.Text);
        if (string.IsNullOrEmpty(key))
            return;

        _templateTextBox.Text = _valueHelper.ResolveTemplate(_exePath, key);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        base.OnFormClosing(e);

        if (DialogResult != DialogResult.OK)
            return;

        if (_addMode)
        {
            var key = HandlerAssociationDialogValueHelper.NormalizeKey(_keyCombo.Text);
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Please enter an association key (e.g. .pdf or http).",
                    "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            if (!AppHandlerRegistrationService.IsValidKey(key))
            {
                MessageBox.Show("Invalid association key. Use a file extension (e.g., .pdf) or protocol name (e.g., http).",
                    "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            SelectedKey = key;
        }

        NewTemplate = HandlerAssociationDialogValueHelper.NormalizeTemplate(_templateTextBox.Text);
        NewPrefixes = _combinedPrefixesSection.GetAssociationPrefixes();
        NewReplacePrefixes = _combinedPrefixesSection.IsReplace;
    }
}

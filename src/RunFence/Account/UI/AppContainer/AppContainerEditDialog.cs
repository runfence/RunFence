using RunFence.Account.Lifecycle;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.UI;

namespace RunFence.Account.UI.AppContainer;

/// <summary>
/// Dialog for creating or editing an AppContainer entry.
/// </summary>
public partial class AppContainerEditDialog : Form
{
    private readonly AppContainerEntry? _existing;
    private readonly IAppContainerService _appContainerService;
    private readonly AppContainerEditService _editService;
    private ToolStripButton? _tsRemove;

    private const int SidRowIndex = 2;
    private const int EphemeralRowIndex = 5;

    // Known COM objects offered in the Add CLSID combobox.
    private static readonly (string Name, string Clsid)[] KnownComObjects =
    [
        ("Shell.Application", "{13709620-C279-11CE-A49E-444553540000}"),
        ("WScript.Shell", "{F935DC22-1CF0-11D0-ADB9-00C04FD58A0B}"),
        ("Scripting.FileSystemObject", "{0D43FE01-F093-11CF-8940-00A0C9054B29}"),
    ];

    private static readonly (string Name, string Sid, bool DefaultOn)[] KnownCapabilities =
    [
        ("internetClient", "S-1-15-3-1", true),
        ("internetClientServer", "S-1-15-3-2", true),
        ("privateNetworkClientServer", "S-1-15-3-3", true),
        ("picturesLibrary", "S-1-15-3-4", false),
        ("videosLibrary", "S-1-15-3-5", false),
        ("musicLibrary", "S-1-15-3-6", false),
        ("documentsLibrary", "S-1-15-3-7", false),
        ("enterpriseAuthentication", "S-1-15-3-8", false),
        ("sharedUserCertificates", "S-1-15-3-9", false),
        ("removableStorage", "S-1-15-3-10", false),
    ];

    private record ComboItemData(string Name, string Clsid)
    {
        public override string ToString() => Name;
    }

    /// <summary>The newly created AppContainerEntry, or null if editing an existing one.</summary>
    public AppContainerEntry? CreatedEntry { get; private set; }

    public bool DeleteRequested { get; private set; }

    public AppContainerEditDialog(AppContainerEntry? existing,
        IAppContainerService appContainerService, AppContainerEditService editService)
    {
        _existing = existing;
        _appContainerService = appContainerService;
        _editService = editService;

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        InitializeCapabilities();
        SetupComToolbar();
        ConfigureMode();

        if (_existing != null)
            PopulateFromExisting();
        else
            SetDefaultCapabilities();

        UpdateProfileNamePreview();
    }

    private void ShowTableRow(int rowIndex, bool visible)
    {
        _layout.RowStyles[rowIndex].SizeType = visible ? SizeType.AutoSize : SizeType.Absolute;
        _layout.RowStyles[rowIndex].Height = 0;
        foreach (Control c in _layout.Controls)
            if (_layout.GetRow(c) == rowIndex)
                c.Visible = visible;
    }

    private void ConfigureMode()
    {
        var isEdit = _existing != null;
        Text = isEdit ? "Edit App Container" : "Create App Container";
        ShowTableRow(SidRowIndex, isEdit);
        ShowTableRow(EphemeralRowIndex, true);
        _deleteButton.Visible = isEdit;
        _profileNameBox.ReadOnly = isEdit;
        _profileNameBox.BackColor = isEdit ? SystemColors.Control : SystemColors.Window;
        if (isEdit)
        {
            _toolTip.SetToolTip(_profileNameBox, "Cannot be changed — determines the container SID.");
            _toolTip.SetToolTip(_sidBox, "Select and copy with Ctrl+C.");
            try
            {
                _sidBox.Text = _appContainerService.GetSid(_existing!.Name);
            }
            catch
            {
                _sidBox.Text = "(unavailable)";
            }
        }
    }

    private void InitializeCapabilities()
    {
        _capCheckBoxes = new CheckBox[KnownCapabilities.Length];
        for (int i = 0; i < KnownCapabilities.Length; i++)
        {
            var cap = KnownCapabilities[i];
            var cb = new CheckBox { Text = cap.Name, Width = 202, AutoSize = false, Tag = cap.Sid, Margin = new Padding(2) };
            _capCheckBoxes[i] = cb;
            _capFlow.Controls.Add(cb);
        }

        _capFlow.Controls.Add(_loopbackCheckBox);
    }

    private void SetupComToolbar()
    {
        var tsAdd = new ToolStripButton
            { Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16), DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Add CLSID…" };
        var tsBrowse = new ToolStripButton
        {
            Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x33, 0x66, 0x99), 16), DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Browse registered COM objects…"
        };
        _tsRemove = new ToolStripButton
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16), DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Remove selected", Enabled = false
        };
        _comToolStrip.Items.AddRange(tsAdd, tsBrowse, new ToolStripSeparator(), _tsRemove);

        _comCustomListBox.SelectedIndexChanged += (_, _) =>
            _tsRemove.Enabled = _comCustomListBox.SelectedIndex >= 0;

        // Context menu: smart visibility — add/browse on empty space, remove on item
        var comCtx = new ContextMenuStrip(components);
        var cmAdd = new ToolStripMenuItem("Add CLSID…") { Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16) };
        var cmBrowse = new ToolStripMenuItem("Browse registered COM objects…") { Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x33, 0x66, 0x99), 16) };
        var cmRemove = new ToolStripMenuItem("Remove") { Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16) };
        comCtx.Items.AddRange(cmAdd, cmBrowse, cmRemove);

        comCtx.Opening += (_, _) =>
        {
            var pt = _comCustomListBox.PointToClient(Cursor.Position);
            var hitIndex = _comCustomListBox.IndexFromPoint(pt);
            var onItem = hitIndex >= 0 && hitIndex < _comCustomListBox.Items.Count;
            if (onItem)
                _comCustomListBox.SelectedIndex = hitIndex;
            cmAdd.Visible = !onItem;
            cmBrowse.Visible = !onItem;
            cmRemove.Visible = onItem;
        };
        _comCustomListBox.ContextMenuStrip = comCtx;

        void DoAddClsid()
        {
            var result = ShowAddClsidPrompt(this);
            if (result == null)
                return;
            if (!ClsidValidator.IsValid(result))
            {
                MessageBox.Show("Enter a valid CLSID in the form {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}.",
                    "Invalid CLSID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_comCustomListBox.Items.Cast<string>().Any(s => string.Equals(s, result, StringComparison.OrdinalIgnoreCase)))
                _comCustomListBox.Items.Add(result);
        }

        void DoBrowse()
        {
            using var dlg = new ComBrowserDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedAppId != null)
            {
                var clsid = dlg.SelectedAppId;
                if (!_comCustomListBox.Items.Cast<string>().Any(s => string.Equals(s, clsid, StringComparison.OrdinalIgnoreCase)))
                    _comCustomListBox.Items.Add(clsid);
            }
        }

        void DoRemove()
        {
            if (_comCustomListBox.SelectedIndex >= 0)
                _comCustomListBox.Items.RemoveAt(_comCustomListBox.SelectedIndex);
        }

        tsAdd.Click += (_, _) => DoAddClsid();
        tsBrowse.Click += (_, _) => DoBrowse();
        _tsRemove.Click += (_, _) => DoRemove();
        cmAdd.Click += (_, _) => DoAddClsid();
        cmBrowse.Click += (_, _) => DoBrowse();
        cmRemove.Click += (_, _) => DoRemove();
    }

    /// <summary>
    /// Shows a compact dialog with a combobox pre-loaded with known COM objects.
    /// User can select a known entry (returns its CLSID) or type a raw CLSID directly.
    /// Changing the text after selecting from the dropdown resets the selection so
    /// the typed text is used as-is.
    /// Returns the CLSID string, or null if the user cancelled.
    /// </summary>
    private static string? ShowAddClsidPrompt(IWin32Window owner)
    {
        using var dlg = new Form();
        dlg.Text = "Add COM Object";
        dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
        dlg.MaximizeBox = false;
        dlg.MinimizeBox = false;
        dlg.StartPosition = FormStartPosition.CenterParent;
        dlg.ClientSize = new Size(380, 90);
        dlg.Padding = new Padding(10);

        var combo = new ComboBox
        {
            Left = 10, Top = 10, Width = 360, Height = 24,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        foreach (var obj in KnownComObjects)
            combo.Items.Add(new ComboItemData(obj.Name, obj.Clsid));

        // When the user types (modifying the text after a dropdown selection), reset
        // the selection so the raw typed text is used rather than the inner CLSID value.
        combo.TextChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboItemData item && combo.Text != item.ToString())
                combo.SelectedIndex = -1;
        };

        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Left = 210, Top = 52, Width = 75, Height = 26 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 295, Top = 52, Width = 75, Height = 26 };
        dlg.Controls.AddRange(combo, ok, cancel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;

        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return null;

        // Known item selected → return its CLSID directly
        if (combo.SelectedItem is ComboItemData selected)
            return selected.Clsid;

        // Raw text → return as-is (caller validates)
        var text = combo.Text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private void OnDisplayNameChanged(object? sender, EventArgs e)
    {
        if (_existing == null)
            UpdateProfileNamePreview();
    }

    private void OnEphemeralChanged(object? sender, EventArgs e)
    {
        if (_existing != null)
            return;
        var isEphemeral = _ephemeralCheckBox?.Checked ?? false;
        _profileNameBox.ReadOnly = isEphemeral;
        _profileNameBox.BackColor = isEphemeral ? SystemColors.Control : SystemColors.Window;
        UpdateProfileNamePreview();
    }

    private void UpdateProfileNamePreview()
    {
        if (_existing != null)
            return;
        if (_ephemeralCheckBox?.Checked == true)
        {
            _profileNameBox.Text = "(auto-generated)";
            return;
        }

        _profileNameBox.Text = GenerateProfileName(_displayNameBox.Text);
    }

    public static string GenerateProfileName(string displayName)
    {
        var sanitized = new string(displayName.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray())
            .Trim('_');
        if (sanitized.Length == 0)
            sanitized = "container";
        if (sanitized.Length > 60)
            sanitized = sanitized[..60];
        return "rfn_" + sanitized;
    }

    private void SetDefaultCapabilities()
    {
        for (int i = 0; i < KnownCapabilities.Length; i++)
            _capCheckBoxes[i].Checked = KnownCapabilities[i].DefaultOn;
        _loopbackCheckBox.Checked = false;
    }

    private void PopulateFromExisting()
    {
        _displayNameBox.Text = _existing!.DisplayName;
        _profileNameBox.Text = _existing.Name;
        _loopbackCheckBox.Checked = _existing.EnableLoopback;
        _ephemeralCheckBox.Checked = _existing.IsEphemeral;

        var existingCaps = _existing.Capabilities ?? [];
        for (int i = 0; i < KnownCapabilities.Length; i++)
            _capCheckBoxes[i].Checked = existingCaps.Contains(KnownCapabilities[i].Sid);

        // All CLSIDs (known and custom) go directly into the list
        foreach (var clsid in _existing.ComAccessClsids ?? [])
            _comCustomListBox.Items.Add(clsid);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var displayName = _displayNameBox.Text.Trim();
        if (string.IsNullOrEmpty(displayName))
        {
            MessageBox.Show("Display name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var isEphemeral = _ephemeralCheckBox?.Checked ?? false;
        var profileName = _existing?.Name
                          ?? (isEphemeral
                              ? "rfn_" + EphemeralNameGenerator.Generate()
                              : GenerateProfileName(displayName));

        var capabilities = _capCheckBoxes
            .Where(cb => cb.Checked)
            .Select(cb => (string)cb.Tag!)
            .ToList();

        var newComClsids = _comCustomListBox.Items.Cast<string>().ToList();

        if (_existing != null)
        {
            var result = _editService.ApplyEditChanges(_existing, displayName, capabilities, _loopbackCheckBox.Checked, newComClsids, isEphemeral);
            if (result.CapabilitiesChanged)
                MessageBox.Show(
                    "Capability changes will take effect on next app launch.",
                    "Restart Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (result.LoopbackFailed)
                MessageBox.Show(
                    $"Failed to {result.LoopbackFailAction} loopback exemption. The setting was not changed.",
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (result.ComErrors.Count > 0)
                MessageBox.Show(
                    $"Some COM access changes could not be applied:\n\n{string.Join("\n", result.ComErrors)}",
                    "COM Access Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            var created = _editService.CreateNewContainer(profileName, displayName, isEphemeral, capabilities,
                _loopbackCheckBox.Checked, newComClsids, out var validationError, out var creationError, out var comErrors);
            if (created == null)
            {
                if (validationError != null)
                    MessageBox.Show(validationError, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show($"Failed to create container: {creationError}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.None;
                return;
            }

            if (comErrors.Count > 0)
                MessageBox.Show(
                    $"Some COM access entries could not be applied:\n\n{string.Join("\n", comErrors)}",
                    "COM Access Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            CreatedEntry = created;
        }

        DialogResult = DialogResult.OK;
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        DeleteRequested = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
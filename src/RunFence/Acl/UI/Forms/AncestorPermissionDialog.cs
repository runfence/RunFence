using System.Security.AccessControl;
using RunFence.Apps.UI;

namespace RunFence.Acl.UI.Forms;

public partial class AncestorPermissionDialog : Form
{
    private readonly IReadOnlyList<string> _ancestors;
    private readonly FileSystemRights _baseRights;

    /// <summary>The path selected by the user, or null if the skip button was chosen.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>The rights to grant: base rights if "Add Permissions" was clicked, base | Write if "Grant Write too" was clicked.</summary>
    public FileSystemRights GrantedRights { get; private set; }

    public AncestorPermissionDialog(string heading, IReadOnlyList<string> ancestors,
        FileSystemRights rights, string skipButtonText = "Launch Without")
    {
        _ancestors = ancestors;
        _baseRights = rights;
        InitializeComponent();
        _skipButton.Text = skipButtonText;
        if ((_baseRights & FileSystemRights.Write) == 0)
            _addWithWriteButton.Visible = true;
        Icon = AppIcons.GetAppIcon();
        _shieldIcon.Image = SystemIcons.Shield.ToBitmap();
        _headingLabel.Text = heading;
        _headingLabel.Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
        _headingLabel.ForeColor = Color.FromArgb(0x00, 0x33, 0x99);

        foreach (var path in ancestors)
            _pathComboBox.Items.Add(Path.GetFileName(path) is { Length: > 0 } name ? name : path);
        _pathComboBox.SelectedIndex = 0;
        _fullPathLabel.Text = ancestors.Count > 0 ? ancestors[0] : string.Empty;
    }

    private void OnPathComboBoxSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_pathComboBox.SelectedIndex >= 0 && _pathComboBox.SelectedIndex < _ancestors.Count)
            _fullPathLabel.Text = _ancestors[_pathComboBox.SelectedIndex];
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (DialogResult is DialogResult.Yes or DialogResult.OK)
        {
            SelectedPath = _pathComboBox.SelectedIndex >= 0 && _pathComboBox.SelectedIndex < _ancestors.Count
                ? _ancestors[_pathComboBox.SelectedIndex]
                : _ancestors.Count > 0
                    ? _ancestors[0]
                    : null;
            GrantedRights = DialogResult == DialogResult.OK
                ? _baseRights | FileSystemRights.Write
                : _baseRights;
        }
    }
}
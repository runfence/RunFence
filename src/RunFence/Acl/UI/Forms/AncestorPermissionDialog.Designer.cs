#nullable disable

using System.ComponentModel;

namespace RunFence.Acl.UI.Forms;

partial class AncestorPermissionDialog
{
    private IContainer components = null;

    private PictureBox _shieldIcon;
    private Label _headingLabel;
    private Label _textLabel;
    private ComboBox _pathComboBox;
    private Label _fullPathLabel;
    private FlowLayoutPanel _buttonPanel;
    private Button _addButton;
    private Button _addWithWriteButton;
    private Button _skipButton;
    private Button _cancelButton;

    private AncestorPermissionDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shieldIcon.Image?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _shieldIcon = new PictureBox();
        _headingLabel = new Label();
        _textLabel = new Label();
        _pathComboBox = new ComboBox();
        _fullPathLabel = new Label();
        _buttonPanel = new FlowLayoutPanel();
        _addButton = new Button();
        _addWithWriteButton = new Button();
        _skipButton = new Button();
        _cancelButton = new Button();

        ((ISupportInitialize)_shieldIcon).BeginInit();
        _buttonPanel.SuspendLayout();
        SuspendLayout();

        // _shieldIcon
        _shieldIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _shieldIcon.Size = new Size(32, 32);
        _shieldIcon.Location = new Point(16, 14);

        // _headingLabel
        _headingLabel.AutoSize = true;
        _headingLabel.Location = new Point(60, 14);

        // _textLabel
        _textLabel.Text = "The selected account does not have the required access.\nSelect a folder to grant access to:";
        _textLabel.AutoSize = true;
        _textLabel.MaximumSize = new Size(384, 0);
        _textLabel.Location = new Point(60, 38);

        // _pathComboBox
        _pathComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _pathComboBox.Location = new Point(60, 86);
        _pathComboBox.Width = 384;
        _pathComboBox.SelectedIndexChanged += OnPathComboBoxSelectedIndexChanged;

        // _fullPathLabel
        _fullPathLabel.AutoSize = true;
        _fullPathLabel.MaximumSize = new Size(384, 0);
        _fullPathLabel.ForeColor = SystemColors.GrayText;
        _fullPathLabel.Location = new Point(60, 114);

        // _addButton
        _addButton.Text = "Add Permissions";
        _addButton.AutoSize = true;
        _addButton.MinimumSize = new Size(0, 28);
        _addButton.Height = 28;
        _addButton.DialogResult = DialogResult.Yes;

        // _addWithWriteButton
        _addWithWriteButton.Text = "Grant Write too";
        _addWithWriteButton.AutoSize = true;
        _addWithWriteButton.MinimumSize = new Size(0, 28);
        _addWithWriteButton.Height = 28;
        _addWithWriteButton.DialogResult = DialogResult.OK;
        _addWithWriteButton.Visible = false;

        // _skipButton
        _skipButton.Text = "Launch Without";
        _skipButton.AutoSize = true;
        _skipButton.MinimumSize = new Size(0, 28);
        _skipButton.Height = 28;
        _skipButton.DialogResult = DialogResult.No;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.MinimumSize = new Size(75, 28);
        _cancelButton.Height = 28;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // _buttonPanel
        // RightToLeft: first added ends up rightmost
        _buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        _buttonPanel.WrapContents = false;
        _buttonPanel.AutoSize = true;
        _buttonPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonPanel.Padding = new Padding(8, 0, 8, 8);
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Controls.AddRange(new Control[] { _cancelButton, _skipButton, _addWithWriteButton, _addButton });

        // AncestorPermissionDialog
        Text = "Permission Required";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true; // Required on secure desktop — custom Forms lose z-order without this
        ClientSize = new Size(560, 210);
        AutoScaleMode = AutoScaleMode.Dpi;
        AcceptButton = _addButton;
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[] { _shieldIcon, _headingLabel, _textLabel, _pathComboBox, _fullPathLabel, _buttonPanel });

        ((ISupportInitialize)_shieldIcon).EndInit();
        _buttonPanel.ResumeLayout(false);
        _buttonPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}

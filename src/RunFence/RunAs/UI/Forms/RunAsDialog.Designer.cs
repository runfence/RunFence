#nullable disable

using System.ComponentModel;

namespace RunFence.RunAs.UI.Forms;

partial class RunAsDialog
{
    private IContainer components = null;

    private Panel _mainPanel;
    private Label _pathHeaderLabel;
    private Label _pathLabel;
    private Label _shortcutLabel;
    private Label _argsLabel;
    private TextBox _argsTextBox;
    private Label _credLabel;
    private ListBox _credentialListBox;
    private CheckBox _updateShortcutCheckBox;
    private CheckBox _showAllAccountsCheckBox;
    private Label _privilegeLevelLabel;
    private ComboBox _privilegeLevelComboBox;
    private Button _revertButton;
    private Button _launchButton;
    private Button _addAppButton;
    private Button _cancelButton;

    private RunAsDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _mainPanel = new Panel();
        _pathHeaderLabel = new Label();
        _pathLabel = new Label();
        _shortcutLabel = new Label();
        _argsLabel = new Label();
        _argsTextBox = new TextBox();
        _credLabel = new Label();
        _credentialListBox = new ListBox();
        _updateShortcutCheckBox = new CheckBox();
        _showAllAccountsCheckBox = new CheckBox();
        _privilegeLevelLabel = new Label();
        _privilegeLevelComboBox = new ComboBox();
        _revertButton = new Button();
        _launchButton = new Button();
        _addAppButton = new Button();
        _cancelButton = new Button();

        _mainPanel.SuspendLayout();
        SuspendLayout();

        // _mainPanel
        _mainPanel.Dock = DockStyle.Fill;
        _mainPanel.Padding = new Padding(15);
        _mainPanel.Controls.AddRange(new Control[]
        {
            _pathHeaderLabel, _pathLabel, _shortcutLabel,
            _argsLabel, _argsTextBox, _credLabel, _credentialListBox,
            _updateShortcutCheckBox, _showAllAccountsCheckBox, _privilegeLevelLabel, _privilegeLevelComboBox,
            _revertButton, _launchButton, _addAppButton, _cancelButton
        });

        // _pathHeaderLabel
        _pathHeaderLabel.Text = "Target:";
        _pathHeaderLabel.AutoSize = true;
        _pathHeaderLabel.Font = new Font(Font, FontStyle.Bold);
        _pathHeaderLabel.Location = new Point(15, 10);

        // _pathLabel
        _pathLabel.AutoSize = true;
        _pathLabel.MaximumSize = new Size(450, 0);
        _pathLabel.Location = new Point(15, 32);

        // _shortcutLabel (shown when shortcutContext != null, position set in code)
        _shortcutLabel.AutoSize = true;
        _shortcutLabel.ForeColor = SystemColors.GrayText;
        _shortcutLabel.Font = new Font(Font, FontStyle.Italic);
        _shortcutLabel.Visible = false;

        // _argsLabel (shown when arguments != null && Count > 0, position set in code)
        _argsLabel.Text = "Arguments:";
        _argsLabel.AutoSize = true;
        _argsLabel.Visible = false;

        // _argsTextBox (shown with _argsLabel, position set in code)
        _argsTextBox.Size = new Size(450, 23);
        _argsTextBox.ReadOnly = true;
        _argsTextBox.Visible = false;

        // _credLabel
        _credLabel.Text = "Run as account:";
        _credLabel.AutoSize = true;
        _credLabel.Location = new Point(15, 56);

        // _credentialListBox
        _credentialListBox.Location = new Point(15, 78);
        _credentialListBox.Size = new Size(450, 210);
        _credentialListBox.IntegralHeight = false;
        _credentialListBox.DrawMode = DrawMode.OwnerDrawFixed;
        _credentialListBox.ItemHeight = 20;
        _credentialListBox.SelectedIndexChanged += OnCredentialSelectionChanged;

        // _updateShortcutCheckBox (shown when shortcutContext != null && !IsAlreadyManaged, position set in code)
        _updateShortcutCheckBox.Text = "Update this shortcut to use launcher";
        _updateShortcutCheckBox.AutoSize = true;
        _updateShortcutCheckBox.Checked = false;
        _updateShortcutCheckBox.Visible = false;
        _updateShortcutCheckBox.CheckedChanged += OnUpdateShortcutCheckBoxChanged;

        // _showAllAccountsCheckBox (shown when WindowsAccountService is available, position set in code)
        _showAllAccountsCheckBox.Text = "Show all accounts";
        _showAllAccountsCheckBox.AutoSize = true;
        _showAllAccountsCheckBox.Checked = false;
        _showAllAccountsCheckBox.Visible = false;
        _showAllAccountsCheckBox.CheckedChanged += OnShowAllAccountsChanged;

        // _privilegeLevelLabel (position set in code)
        _privilegeLevelLabel.Text = "Privilege level:";
        _privilegeLevelLabel.AutoSize = true;
        _privilegeLevelLabel.Location = new Point(15, 300);

        // _privilegeLevelComboBox (position set in code)
        _privilegeLevelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _privilegeLevelComboBox.Size = new Size(200, 23);
        _privilegeLevelComboBox.Location = new Point(265, 295);
        _privilegeLevelComboBox.Items.AddRange(new object[] { "Highest Allowed", "Basic", "Low Integrity" });

        // _revertButton (shown when shortcutContext.IsAlreadyManaged && ManagedApp != null)
        _revertButton.Text = "Revert Shortcut";
        _revertButton.Size = new Size(120, 28);
        _revertButton.FlatStyle = FlatStyle.System;
        _revertButton.Visible = false;
        _revertButton.Location = new Point(15, 355);
        _revertButton.Click += OnRevertClick;

        // _launchButton
        _launchButton.Text = "Launch";
        _launchButton.Size = new Size(90, 28);
        _launchButton.FlatStyle = FlatStyle.System;
        _launchButton.Enabled = false;
        _launchButton.Location = new Point(155, 355);
        _launchButton.Click += OnLaunchClick;

        // _addAppButton
        _addAppButton.Text = "Add app entry\u2026";
        _addAppButton.Size = new Size(120, 28);
        _addAppButton.FlatStyle = FlatStyle.System;
        _addAppButton.Enabled = false;
        _addAppButton.Location = new Point(250, 355);
        _addAppButton.Click += OnAddAppClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(90, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Location = new Point(375, 355);
        _cancelButton.Click += OnCancelClick;

        // RunAsDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "RunFence";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Size = new Size(500, 400);
        AcceptButton = _launchButton;
        CancelButton = _cancelButton;
        Controls.Add(_mainPanel);

        _mainPanel.ResumeLayout(false);
        _mainPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}

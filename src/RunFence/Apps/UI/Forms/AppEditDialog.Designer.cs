#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class AppEditDialog
{
    private IContainer components = null;

    private Label _nameLabel;
    private TextBox _nameTextBox;
    private Label _filePathLabel;
    private TextBox _filePathTextBox;
    private Button _browseButton;
    private Button _browseFolderButton;
    private Button _discoverButton;
    private Label _accountLabel;
    private ComboBox _accountComboBox;
    private Label _configLabel;
    private ComboBox _configComboBox;
    private CheckBox _manageShortcutsCheckBox;
    private CheckBox _launchAsLowIlCheckBox;
    private CheckBox _splitTokenCheckBox;
    private TabControl _tabControl;
    private TabPage _tabMain;
    private TabPage _tabParameters;
    private TabPage _tabAccess;
    private TabPage _tabAssociations;
    private Label _defaultArgsLabel;
    private TextBox _defaultArgsTextBox;
    private CheckBox _allowPassArgsCheckBox;
    private Label _argsTemplateLabel;
    private TextBox _argsTemplateTextBox;
    private CheckBox _allowPassWorkDirCheckBox;
    private Label _workingDirLabel;
    private TextBox _workingDirTextBox;
    private Button _workingDirBrowseButton;
    private Label _launcherPathLabel;
    private TextBox _launcherPathTextBox;
    private Label _launcherArgsLabel;
    private TextBox _launcherArgsTextBox;
    private CheckBox _overrideIpcCallersCheckBox;
    private Panel _ipcContainer;
    private Panel _envVarsContainer;
    private Label _statusLabel;
    private CheckBox _launchNowCheckBox;
    private Button _okButton;
    private Button _cancelButton;
    private Button _removeButton;

    private AppEditDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _configToolTip?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _nameLabel = new Label();
        _nameTextBox = new TextBox();
        _filePathLabel = new Label();
        _filePathTextBox = new TextBox();
        _browseButton = new Button();
        _browseFolderButton = new Button();
        _discoverButton = new Button();
        _accountLabel = new Label();
        _accountComboBox = new ComboBox();
        _configLabel = new Label();
        _configComboBox = new ComboBox();
        _manageShortcutsCheckBox = new CheckBox();
        _launchAsLowIlCheckBox = new CheckBox();
        _splitTokenCheckBox = new CheckBox();
        _tabControl = new TabControl();
        _tabMain = new TabPage();
        _tabParameters = new TabPage();
        _tabAccess = new TabPage();
        _tabAssociations = new TabPage();
        _defaultArgsLabel = new Label();
        _defaultArgsTextBox = new TextBox();
        _allowPassArgsCheckBox = new CheckBox();
        _argsTemplateLabel = new Label();
        _argsTemplateTextBox = new TextBox();
        _allowPassWorkDirCheckBox = new CheckBox();
        _workingDirLabel = new Label();
        _workingDirTextBox = new TextBox();
        _workingDirBrowseButton = new Button();
        _launcherPathLabel = new Label();
        _launcherPathTextBox = new TextBox();
        _launcherArgsLabel = new Label();
        _launcherArgsTextBox = new TextBox();
        _overrideIpcCallersCheckBox = new CheckBox();
        _ipcContainer = new Panel();
        _envVarsContainer = new Panel();
        _statusLabel = new Label();
        _launchNowCheckBox = new CheckBox();
        _okButton = new Button();
        _cancelButton = new Button();
        _removeButton = new Button();

        SuspendLayout();
        _tabControl.SuspendLayout();
        _tabMain.SuspendLayout();
        _tabParameters.SuspendLayout();
        _tabAccess.SuspendLayout();

        // Form
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(510, 640);

        // _tabControl
        _tabControl.Location = new Point(0, 0);
        _tabControl.Size = new Size(510, 584);
        _tabControl.Controls.AddRange(new TabPage[] { _tabMain, _tabParameters, _tabAccess, _tabAssociations });

        // _tabMain
        _tabMain.Text = "General";

        // _tabParameters
        _tabParameters.Text = "Parameters";

        // _tabAccess
        _tabAccess.Text = "Access";
        _tabAccess.AutoScroll = true;

        // _tabAssociations
        _tabAssociations.Text = "Associations";

        // --- Main tab controls ---

        // _nameLabel
        _nameLabel.Text = "Name:";
        _nameLabel.Location = new Point(10, 10);
        _nameLabel.AutoSize = true;

        // _nameTextBox
        _nameTextBox.Location = new Point(10, 30);
        _nameTextBox.Size = new Size(480, 23);

        // _filePathLabel (Text overridden in code for folder mode)
        _filePathLabel.Text = "File Path or URL:";
        _filePathLabel.Location = new Point(10, 63);
        _filePathLabel.AutoSize = true;

        // _filePathTextBox
        _filePathTextBox.Location = new Point(10, 83);
        _filePathTextBox.Size = new Size(200, 23);
        _filePathTextBox.TextChanged += OnFilePathChanged;

        // _browseButton
        _browseButton.Text = "Browse...";
        _browseButton.Location = new Point(218, 81);
        _browseButton.Size = new Size(80, 27);
        _browseButton.FlatStyle = FlatStyle.System;
        _browseButton.Click += OnBrowseClick;

        // _browseFolderButton
        _browseFolderButton.Text = "Folder...";
        _browseFolderButton.Location = new Point(303, 81);
        _browseFolderButton.Size = new Size(75, 27);
        _browseFolderButton.FlatStyle = FlatStyle.System;
        _browseFolderButton.Click += OnBrowseFolderClick;

        // _discoverButton
        _discoverButton.Text = "Discover...";
        _discoverButton.Location = new Point(383, 81);
        _discoverButton.Size = new Size(97, 27);
        _discoverButton.FlatStyle = FlatStyle.System;
        _discoverButton.Click += OnDiscoverClick;

        // _accountLabel
        _accountLabel.Text = "Run As Account:";
        _accountLabel.Location = new Point(10, 116);
        _accountLabel.AutoSize = true;

        // _accountComboBox (items populated in code)
        _accountComboBox.Location = new Point(10, 136);
        _accountComboBox.Size = new Size(480, 23);
        _accountComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        // _configLabel
        _configLabel.Text = "Config File:";
        _configLabel.Location = new Point(10, 169);
        _configLabel.AutoSize = true;

        // _configComboBox (items, Enabled, and tooltip configured in code)
        _configComboBox.Location = new Point(10, 189);
        _configComboBox.Size = new Size(480, 23);
        _configComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        // _workingDirLabel
        _workingDirLabel.Text = "Working Directory (optional, defaults to exe folder):";
        _workingDirLabel.Location = new Point(10, 222);
        _workingDirLabel.AutoSize = true;

        // _workingDirTextBox
        _workingDirTextBox.Location = new Point(10, 242);
        _workingDirTextBox.Size = new Size(375, 23);

        // _workingDirBrowseButton
        _workingDirBrowseButton.Text = "Browse...";
        _workingDirBrowseButton.Location = new Point(390, 240);
        _workingDirBrowseButton.Size = new Size(90, 27);
        _workingDirBrowseButton.FlatStyle = FlatStyle.System;
        _workingDirBrowseButton.Click += OnWorkingDirBrowseClick;

        // _allowPassWorkDirCheckBox
        _allowPassWorkDirCheckBox.Text = "Allow launcher to pass working directory (overrides default)";
        _allowPassWorkDirCheckBox.Location = new Point(10, 270);
        _allowPassWorkDirCheckBox.AutoSize = true;

        // _manageShortcutsCheckBox
        _manageShortcutsCheckBox.Text = "Manage shortcuts (redirect to launcher)";
        _manageShortcutsCheckBox.Location = new Point(10, 298);
        _manageShortcutsCheckBox.AutoSize = true;
        _manageShortcutsCheckBox.Checked = true;

        // _launchAsLowIlCheckBox (three-state for null/true/false)
        _launchAsLowIlCheckBox.Text = "Launch as low integrity level";
        _launchAsLowIlCheckBox.ThreeState = true;
        _launchAsLowIlCheckBox.Location = new Point(10, 326);
        _launchAsLowIlCheckBox.AutoSize = true;

        // _splitTokenCheckBox (three-state for null/true/false)
        _splitTokenCheckBox.Text = "De-elevate";
        _splitTokenCheckBox.Location = new Point(10, 351);
        _splitTokenCheckBox.AutoSize = true;
        _splitTokenCheckBox.ThreeState = true;

        // _launcherPathLabel
        _launcherPathLabel.Text = "Launcher Path:";
        _launcherPathLabel.Location = new Point(10, 377);
        _launcherPathLabel.AutoSize = true;

        // _launcherPathTextBox (Text set in code: launcher application path)
        _launcherPathTextBox.Location = new Point(10, 397);
        _launcherPathTextBox.Size = new Size(480, 23);
        _launcherPathTextBox.ReadOnly = true;
        _launcherPathTextBox.BackColor = SystemColors.Control;

        // _launcherArgsLabel
        _launcherArgsLabel.Text = "Launcher Arguments (App ID):";
        _launcherArgsLabel.Location = new Point(10, 425);
        _launcherArgsLabel.AutoSize = true;

        // _launcherArgsTextBox (Text and ForeColor set in code based on _existing)
        _launcherArgsTextBox.Location = new Point(10, 445);
        _launcherArgsTextBox.Size = new Size(480, 23);
        _launcherArgsTextBox.ReadOnly = true;
        _launcherArgsTextBox.BackColor = SystemColors.Control;

        // Add controls to _tabMain
        _tabMain.Controls.Add(_nameLabel);
        _tabMain.Controls.Add(_nameTextBox);
        _tabMain.Controls.Add(_filePathLabel);
        _tabMain.Controls.Add(_filePathTextBox);
        _tabMain.Controls.Add(_browseButton);
        _tabMain.Controls.Add(_browseFolderButton);
        _tabMain.Controls.Add(_discoverButton);
        _tabMain.Controls.Add(_accountLabel);
        _tabMain.Controls.Add(_accountComboBox);
        _tabMain.Controls.Add(_configLabel);
        _tabMain.Controls.Add(_configComboBox);
        _tabMain.Controls.Add(_workingDirLabel);
        _tabMain.Controls.Add(_workingDirTextBox);
        _tabMain.Controls.Add(_workingDirBrowseButton);
        _tabMain.Controls.Add(_allowPassWorkDirCheckBox);
        _tabMain.Controls.Add(_manageShortcutsCheckBox);
        _tabMain.Controls.Add(_launchAsLowIlCheckBox);
        _tabMain.Controls.Add(_splitTokenCheckBox);
        _tabMain.Controls.Add(_launcherPathLabel);
        _tabMain.Controls.Add(_launcherPathTextBox);
        _tabMain.Controls.Add(_launcherArgsLabel);
        _tabMain.Controls.Add(_launcherArgsTextBox);

        // --- Parameters tab controls ---

        // _defaultArgsLabel
        _defaultArgsLabel.Text = "Default Arguments:";
        _defaultArgsLabel.Location = new Point(10, 15);
        _defaultArgsLabel.AutoSize = true;

        // _defaultArgsTextBox
        _defaultArgsTextBox.Location = new Point(10, 35);
        _defaultArgsTextBox.Size = new Size(480, 23);

        // _allowPassArgsCheckBox
        _allowPassArgsCheckBox.Text = "Allow launcher to pass arguments (replaces default when provided)";
        _allowPassArgsCheckBox.Location = new Point(10, 68);
        _allowPassArgsCheckBox.AutoSize = true;

        // _argsTemplateLabel
        _argsTemplateLabel.Text = "Arguments template (use %1 for passed args, otherwise appended):";
        _argsTemplateLabel.Location = new Point(10, 98);
        _argsTemplateLabel.AutoSize = true;

        // _argsTemplateTextBox
        _argsTemplateTextBox.Location = new Point(10, 118);
        _argsTemplateTextBox.Size = new Size(480, 23);

        // _envVarsContainer (EnvVarsSection added in code; fills remaining height in tab)
        _envVarsContainer.Location = new Point(10, 152);
        _envVarsContainer.Size = new Size(480, 377);
        _envVarsContainer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        // Add controls to _tabParameters
        _tabParameters.Controls.Add(_defaultArgsLabel);
        _tabParameters.Controls.Add(_defaultArgsTextBox);
        _tabParameters.Controls.Add(_allowPassArgsCheckBox);
        _tabParameters.Controls.Add(_argsTemplateLabel);
        _tabParameters.Controls.Add(_argsTemplateTextBox);
        _tabParameters.Controls.Add(_envVarsContainer);

        // --- Access tab controls ---
        // Layout: IPC override checkbox + container at top (fixed), AclSection below.
        // Positions are overridden in code (AppEditDialog.cs constructor).

        // _overrideIpcCallersCheckBox (placeholder position; set in code)
        _overrideIpcCallersCheckBox.Text = "Custom launcher access list (overrides global)";
        _overrideIpcCallersCheckBox.Location = new Point(10, 10);
        _overrideIpcCallersCheckBox.AutoSize = true;
        _overrideIpcCallersCheckBox.CheckedChanged += OnIpcOverrideChanged;

        // _ipcContainer (IpcCallerSection added in code; placeholder position; set in code)
        _ipcContainer.Location = new Point(10, 32);
        _ipcContainer.Size = new Size(480, 120);
        _ipcContainer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Add IPC controls to _tabAccess (AclSection added in code below ipcContainer)
        _tabAccess.Controls.Add(_overrideIpcCallersCheckBox);
        _tabAccess.Controls.Add(_ipcContainer);

        // --- Associations tab is populated entirely in code ---

        // --- Bottom form controls ---

        // _statusLabel
        _statusLabel.Size = new Size(480, 16);
        _statusLabel.ForeColor = Color.Red;
        _statusLabel.Location = new Point(15, 586);

        // _launchNowCheckBox (position set in code relative to OK button)
        _launchNowCheckBox.Text = "Launch now";
        _launchNowCheckBox.AutoSize = true;
        _launchNowCheckBox.Location = new Point(190, 611);

        // _okButton
        _okButton.Text = "OK";
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Location = new Point(330, 606);
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Location = new Point(415, 606);

        // _removeButton (Visible set in code)
        _removeButton.Text = "Remove";
        _removeButton.Size = new Size(75, 28);
        _removeButton.FlatStyle = FlatStyle.System;
        _removeButton.Location = new Point(15, 606);
        _removeButton.Visible = false;
        _removeButton.Click += OnRemoveClick;

        // Add controls to Form
        Controls.Add(_tabControl);
        Controls.Add(_statusLabel);
        Controls.Add(_launchNowCheckBox);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);
        Controls.Add(_removeButton);

        _tabAccess.ResumeLayout(false);
        _tabParameters.ResumeLayout(false);
        _tabMain.ResumeLayout(false);
        _tabMain.PerformLayout();
        _tabControl.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

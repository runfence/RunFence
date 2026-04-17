#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class OptionsPanel
{
    private IContainer components = null;

    private CheckBox _autoStartCheckBox;
    private CheckBox _enableLoggingCheckBox;
    private Button _openLogButton;
    private CheckBox _idleTimeoutCheckBox;
    private NumericUpDown _idleTimeoutUpDown;
    private CheckBox _autoLockCheckBox;
    private NumericUpDown _autoLockTimeoutUpDown;
    private ComboBox _unlockModeComboBox;
    private CheckBox _contextMenuCheckBox;
    private TextBox _folderBrowserExeTextBox;
    private TextBox _folderBrowserArgsTextBox;
    private TextBox _defaultSettingsPathTextBox;
    private ToolTip _tooltip;
    private Button _changePinBtn;
    private Button _cleanupBtn;
    private Button _securityCheckBtn;
    private Button _folderBrowseButton;
    private Button _settingsBrowseButton;
    private Button _exportSettingsButton;
    private Panel _callerGroup;
    private Panel _rightConfigPanel;
    private Panel _dragBridgePlaceholder;
    private GroupBox _startupGroup;
    private GroupBox _pinGroup;
    private GroupBox _autoLockGroup;
    private GroupBox _idleTimeoutGroup;
    private GroupBox _contextMenuGroup;
    private GroupBox _folderBrowserGroup;
    private GroupBox _desktopSettingsGroup;
    private Label _lockTimeoutLabel;
    private Label _idleMinLabel;
    private Label _folderBrowserExeLabel;
    private Label _folderBrowserArgsLabel;
    private Label _settingsPathLabel;
    private TableLayoutPanel _startupPinContainer;
    private TableLayoutPanel _autoLockDragBridgeContainer;
    private TableLayoutPanel _dragBridgeFirewallContainer;
    private TableLayoutPanel _idleCtxContainer;
    private TableLayoutPanel _folderSettingsContainer;
    private TableLayoutPanel _mainFillPanel;
    private GroupBox _firewallGroup;
    private CheckBox _blockIcmpCheckBox;
    private ToolTip _blockIcmpCheckBoxTooltip;
    private Panel _spacer1;
    private Panel _spacer2;
    private Panel _spacer3;

    private OptionsPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsHandler?.FlushPendingSave(SaveSettings);
            _tooltip?.Dispose();
            _blockIcmpCheckBoxTooltip?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _autoStartCheckBox = new CheckBox();
        _enableLoggingCheckBox = new CheckBox();
        _openLogButton = new Button();
        _idleTimeoutCheckBox = new CheckBox();
        _idleTimeoutUpDown = new NumericUpDown();
        _autoLockCheckBox = new CheckBox();
        _autoLockTimeoutUpDown = new NumericUpDown();
        _unlockModeComboBox = new ComboBox();
        _contextMenuCheckBox = new CheckBox();
        _folderBrowserExeTextBox = new TextBox();
        _folderBrowserArgsTextBox = new TextBox();
        _defaultSettingsPathTextBox = new TextBox();
        _tooltip = new ToolTip();
        _changePinBtn = new Button();
        _cleanupBtn = new Button();
        _securityCheckBtn = new Button();
        _folderBrowseButton = new Button();
        _settingsBrowseButton = new Button();
        _exportSettingsButton = new Button();
        _callerGroup = new Panel();
        _rightConfigPanel = new Panel();
        _dragBridgePlaceholder = new Panel();
        _startupGroup = new GroupBox();
        _pinGroup = new GroupBox();
        _autoLockGroup = new GroupBox();
        _idleTimeoutGroup = new GroupBox();
        _contextMenuGroup = new GroupBox();
        _folderBrowserGroup = new GroupBox();
        _desktopSettingsGroup = new GroupBox();
        _lockTimeoutLabel = new Label();
        _idleMinLabel = new Label();
        _folderBrowserExeLabel = new Label();
        _folderBrowserArgsLabel = new Label();
        _settingsPathLabel = new Label();
        _startupPinContainer = new TableLayoutPanel();
        _autoLockDragBridgeContainer = new TableLayoutPanel();
        _dragBridgeFirewallContainer = new TableLayoutPanel();
        _idleCtxContainer = new TableLayoutPanel();
        _folderSettingsContainer = new TableLayoutPanel();
        _mainFillPanel = new TableLayoutPanel();
        _firewallGroup = new GroupBox();
        _blockIcmpCheckBox = new CheckBox();
        _spacer1 = new Panel();
        _spacer2 = new Panel();
        _spacer3 = new Panel();

        ((ISupportInitialize)_idleTimeoutUpDown).BeginInit();
        ((ISupportInitialize)_autoLockTimeoutUpDown).BeginInit();
        SuspendLayout();

        // --- Startup group ---
        _startupGroup.Text = "Startup";
        _startupGroup.Dock = DockStyle.Fill;

        _autoStartCheckBox.Text = "Auto-start on login";
        _autoStartCheckBox.Location = new Point(15, 25);
        _autoStartCheckBox.AutoSize = true;
        _startupGroup.Controls.Add(_autoStartCheckBox);

        _enableLoggingCheckBox.Text = "Enable logging";
        _enableLoggingCheckBox.Location = new Point(200, 25);
        _enableLoggingCheckBox.AutoSize = true;
        _startupGroup.Controls.Add(_enableLoggingCheckBox);

        _openLogButton.Text = "Open Log";
        _openLogButton.Location = new Point(0, 22);
        _openLogButton.Size = new Size(80, 27);
        _openLogButton.FlatStyle = FlatStyle.System;
        _openLogButton.Click += OnOpenLogClick;
        _startupGroup.Controls.Add(_openLogButton);

        // --- Controls group ---
        _pinGroup.Text = "Controls";
        _pinGroup.Dock = DockStyle.Fill;

        _changePinBtn.Text = "  Change PIN";
        _changePinBtn.Location = new Point(10, 22);
        _changePinBtn.Size = new Size(120, 34);
        _changePinBtn.FlatStyle = FlatStyle.Standard;
        _changePinBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _changePinBtn.TextAlign = ContentAlignment.MiddleLeft;
        _changePinBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _changePinBtn.Click += OnChangePinClick;
        _pinGroup.Controls.Add(_changePinBtn);

        _securityCheckBtn.Text = "  Security Check";
        _securityCheckBtn.Location = new Point(140, 22);
        _securityCheckBtn.Size = new Size(145, 34);
        _securityCheckBtn.FlatStyle = FlatStyle.Standard;
        _securityCheckBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _securityCheckBtn.TextAlign = ContentAlignment.MiddleLeft;
        _securityCheckBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _securityCheckBtn.Click += OnSecurityCheckClick;
        _pinGroup.Controls.Add(_securityCheckBtn);

        _cleanupBtn.Text = "  Cleanup && Exit";
        _cleanupBtn.Location = new Point(295, 22);
        _cleanupBtn.Size = new Size(140, 34);
        _cleanupBtn.FlatStyle = FlatStyle.Standard;
        _cleanupBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _cleanupBtn.TextAlign = ContentAlignment.MiddleLeft;
        _cleanupBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _cleanupBtn.Click += OnCleanupClick;
        _pinGroup.Controls.Add(_cleanupBtn);

        _startupPinContainer.Dock = DockStyle.Top;
        _startupPinContainer.Height = 65;
        _startupPinContainer.ColumnCount = 2;
        _startupPinContainer.RowCount = 1;
        _startupPinContainer.Margin = Padding.Empty;
        _startupPinContainer.Padding = Padding.Empty;
        _startupPinContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _startupPinContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _startupPinContainer.Controls.Add(_pinGroup, 0, 0);
        _startupPinContainer.Controls.Add(_startupGroup, 1, 0);

        // --- Auto-Lock group ---
        _autoLockGroup.Text = "Auto-Lock";
        _autoLockGroup.Dock = DockStyle.Fill;

        _autoLockCheckBox.Text = "Lock when minimized";
        _autoLockCheckBox.Location = new Point(15, 25);
        _autoLockCheckBox.AutoSize = true;
        _autoLockGroup.Controls.Add(_autoLockCheckBox);

        _lockTimeoutLabel.Text = "Delay (min):";
        _lockTimeoutLabel.Location = new Point(185, 26);
        _lockTimeoutLabel.AutoSize = true;
        _autoLockGroup.Controls.Add(_lockTimeoutLabel);

        _autoLockTimeoutUpDown.Location = new Point(283, 23);
        _autoLockTimeoutUpDown.Width = 55;
        _autoLockTimeoutUpDown.Minimum = 0;
        _autoLockTimeoutUpDown.Maximum = 999;
        _autoLockTimeoutUpDown.Value = 0;
        _autoLockTimeoutUpDown.Enabled = false;
        _autoLockGroup.Controls.Add(_autoLockTimeoutUpDown);

        _unlockModeComboBox.Location = new Point(348, 22);
        _unlockModeComboBox.Width = 115;
        _unlockModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _unlockModeComboBox.Items.AddRange(new object[] { "UAC (Admin)", "PIN", "UAC + PIN", "Windows Hello" });
        _unlockModeComboBox.SelectedIndex = 0;
        _autoLockGroup.Controls.Add(_unlockModeComboBox);

        // --- Firewall group ---
        _firewallGroup.Text = "Firewall";
        _firewallGroup.Dock = DockStyle.Fill;

        _blockIcmpCheckBox.Text = "Block ICMP when Internet is blocked";
        _blockIcmpCheckBox.Location = new Point(15, 24);
        _blockIcmpCheckBox.AutoSize = true;
        _firewallGroup.Controls.Add(_blockIcmpCheckBox);
        
        _blockIcmpCheckBoxTooltip = new  ToolTip();
        _blockIcmpCheckBoxTooltip.SetToolTip(_blockIcmpCheckBox, "ICMP tunneling can be potentially used to escape Internet restrictions");

        // --- Drag Bridge placeholder (section added in code) + Firewall split ---
        _dragBridgePlaceholder.Dock = DockStyle.Fill;

        _dragBridgeFirewallContainer.Dock = DockStyle.Fill;
        _dragBridgeFirewallContainer.ColumnCount = 2;
        _dragBridgeFirewallContainer.RowCount = 1;
        _dragBridgeFirewallContainer.Margin = Padding.Empty;
        _dragBridgeFirewallContainer.Padding = Padding.Empty;
        _dragBridgeFirewallContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _dragBridgeFirewallContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _dragBridgeFirewallContainer.Controls.Add(_dragBridgePlaceholder, 0, 0);
        _dragBridgeFirewallContainer.Controls.Add(_firewallGroup, 1, 0);

        _autoLockDragBridgeContainer.Dock = DockStyle.Top;
        _autoLockDragBridgeContainer.Height = 55;
        _autoLockDragBridgeContainer.ColumnCount = 2;
        _autoLockDragBridgeContainer.RowCount = 1;
        _autoLockDragBridgeContainer.Margin = Padding.Empty;
        _autoLockDragBridgeContainer.Padding = Padding.Empty;
        _autoLockDragBridgeContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _autoLockDragBridgeContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _autoLockDragBridgeContainer.Controls.Add(_autoLockGroup, 0, 0);
        _autoLockDragBridgeContainer.Controls.Add(_dragBridgeFirewallContainer, 1, 0);

        // --- Idle Timeout group ---
        _idleTimeoutGroup.Text = "Idle Timeout";
        _idleTimeoutGroup.Dock = DockStyle.Fill;

        _idleTimeoutCheckBox.Text = "Exit after app idle";
        _idleTimeoutCheckBox.Location = new Point(15, 25);
        _idleTimeoutCheckBox.AutoSize = true;
        _idleTimeoutGroup.Controls.Add(_idleTimeoutCheckBox);

        _idleMinLabel.Text = "Minutes:";
        _idleMinLabel.Location = new Point(185, 26);
        _idleMinLabel.AutoSize = true;
        _idleTimeoutGroup.Controls.Add(_idleMinLabel);

        _idleTimeoutUpDown.Location = new Point(258, 23);
        _idleTimeoutUpDown.Width = 65;
        _idleTimeoutUpDown.Minimum = 1;
        _idleTimeoutUpDown.Maximum = 999;
        _idleTimeoutUpDown.Value = 30;
        _idleTimeoutUpDown.Enabled = false;
        _idleTimeoutGroup.Controls.Add(_idleTimeoutUpDown);

        // --- Explorer Integration group ---
        _contextMenuGroup.Text = "Explorer Integration";
        _contextMenuGroup.Dock = DockStyle.Fill;

        _contextMenuCheckBox.Text = "Enable 'RunFence...' context menu for files";
        _contextMenuCheckBox.Location = new Point(15, 25);
        _contextMenuCheckBox.AutoSize = true;
        _contextMenuGroup.Controls.Add(_contextMenuCheckBox);

        _idleCtxContainer.Dock = DockStyle.Top;
        _idleCtxContainer.Height = 60;
        _idleCtxContainer.ColumnCount = 2;
        _idleCtxContainer.RowCount = 1;
        _idleCtxContainer.Margin = Padding.Empty;
        _idleCtxContainer.Padding = Padding.Empty;
        _idleCtxContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _idleCtxContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _idleCtxContainer.Controls.Add(_idleTimeoutGroup, 0, 0);
        _idleCtxContainer.Controls.Add(_contextMenuGroup, 1, 0);

        // --- Folder Browser group ---
        _folderBrowserGroup.Text = "Folder Browser";
        _folderBrowserGroup.Dock = DockStyle.Fill;

        _folderBrowserExeLabel.Text = "Application used to open folder entries:";
        _folderBrowserExeLabel.Location = new Point(15, 22);
        _folderBrowserExeLabel.AutoSize = true;
        _folderBrowserGroup.Controls.Add(_folderBrowserExeLabel);

        _folderBrowserExeTextBox.Location = new Point(15, 42);
        _folderBrowserExeTextBox.Size = new Size(350, 23);
        _folderBrowserGroup.Controls.Add(_folderBrowserExeTextBox);

        _folderBrowseButton.Text = "Browse...";
        _folderBrowseButton.Location = new Point(375, 40);
        _folderBrowseButton.Size = new Size(80, 27);
        _folderBrowseButton.FlatStyle = FlatStyle.System;
        _folderBrowseButton.Click += OnFolderBrowserBrowseClick;
        _folderBrowserGroup.Controls.Add(_folderBrowseButton);

        _folderBrowserArgsLabel.Text = "Arguments (%1 = folder path):";
        _folderBrowserArgsLabel.Location = new Point(15, 72);
        _folderBrowserArgsLabel.AutoSize = true;
        _folderBrowserGroup.Controls.Add(_folderBrowserArgsLabel);

        _folderBrowserArgsTextBox.Location = new Point(235, 69);
        _folderBrowserArgsTextBox.Size = new Size(220, 23);
        _folderBrowserGroup.Controls.Add(_folderBrowserArgsTextBox);

        // --- Desktop Settings group ---
        _desktopSettingsGroup.Text = "Desktop Settings";
        _desktopSettingsGroup.Dock = DockStyle.Fill;

        _settingsPathLabel.Text = "Default file for new account settings import:";
        _settingsPathLabel.Location = new Point(15, 22);
        _settingsPathLabel.AutoSize = true;
        _desktopSettingsGroup.Controls.Add(_settingsPathLabel);

        _defaultSettingsPathTextBox.Location = new Point(15, 42);
        _defaultSettingsPathTextBox.Size = new Size(350, 23);
        _desktopSettingsGroup.Controls.Add(_defaultSettingsPathTextBox);

        _settingsBrowseButton.Text = "Browse...";
        _settingsBrowseButton.Location = new Point(375, 40);
        _settingsBrowseButton.Size = new Size(80, 27);
        _settingsBrowseButton.FlatStyle = FlatStyle.System;
        _settingsBrowseButton.Click += OnDefaultSettingsPathBrowseClick;
        _desktopSettingsGroup.Controls.Add(_settingsBrowseButton);

        _exportSettingsButton.Text = "Export Current Desktop Settings As...";
        _exportSettingsButton.Location = new Point(15, 72);
        _exportSettingsButton.Size = new Size(300, 27);
        _exportSettingsButton.FlatStyle = FlatStyle.System;
        _exportSettingsButton.Click += OnExportDesktopSettingsClick;
        _desktopSettingsGroup.Controls.Add(_exportSettingsButton);

        _folderSettingsContainer.Dock = DockStyle.Top;
        _folderSettingsContainer.Height = 105;
        _folderSettingsContainer.ColumnCount = 2;
        _folderSettingsContainer.RowCount = 1;
        _folderSettingsContainer.Margin = Padding.Empty;
        _folderSettingsContainer.Padding = Padding.Empty;
        _folderSettingsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _folderSettingsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _folderSettingsContainer.Controls.Add(_folderBrowserGroup, 0, 0);
        _folderSettingsContainer.Controls.Add(_desktopSettingsGroup, 1, 0);

        // --- Caller group (section placeholder) ---
        _callerGroup.Dock = DockStyle.Fill;

        // --- Right config panel (section placeholder) ---
        _rightConfigPanel.Dock = DockStyle.Fill;

        _mainFillPanel.Dock = DockStyle.Fill;
        _mainFillPanel.ColumnCount = 2;
        _mainFillPanel.RowCount = 1;
        _mainFillPanel.Margin = Padding.Empty;
        _mainFillPanel.Padding = Padding.Empty;
        _mainFillPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _mainFillPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _mainFillPanel.Controls.Add(_callerGroup, 0, 0);
        _mainFillPanel.Controls.Add(_rightConfigPanel, 1, 0);

        // Spacers
        _spacer1.Dock = DockStyle.Top;
        _spacer1.Height = 3;

        _spacer2.Dock = DockStyle.Top;
        _spacer2.Height = 3;

        _spacer3.Dock = DockStyle.Top;
        _spacer3.Height = 3;

        // Add in reverse order for DockStyle.Top (last added = topmost)
        Controls.Add(_mainFillPanel);
        Controls.Add(_folderSettingsContainer);
        Controls.Add(_spacer1);
        Controls.Add(_idleCtxContainer);
        Controls.Add(_spacer2);
        Controls.Add(_autoLockDragBridgeContainer);
        Controls.Add(_spacer3);
        Controls.Add(_startupPinContainer);

        // OptionsPanel
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoScroll = true;
        Dock = DockStyle.Fill;

        ((ISupportInitialize)_idleTimeoutUpDown).EndInit();
        ((ISupportInitialize)_autoLockTimeoutUpDown).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}

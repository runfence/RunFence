#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class OptionsPanel
{
    private IContainer components = null;

    private CheckBox _autoStartCheckBox;
    private CheckBox _startWithoutPinCheckBox;
    private Label _logVerbosityLabel;
    private ComboBox _logVerbosityComboBox;
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
    private Button _migrateAccountBtn;
    private TableLayoutPanel _controlsButtonPanel;
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
    private Panel _spacer1;
    private Panel _spacer2;
    private Panel _spacer3;
    private TableLayoutPanel _folderBrowserExePanel;
    private TableLayoutPanel _folderBrowserArgsPanel;
    private TableLayoutPanel _desktopSettingsPathPanel;

    private OptionsPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsHandler?.FlushPendingSave(SaveSettings);
            _tooltip?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _autoStartCheckBox = new CheckBox();
        _startWithoutPinCheckBox = new CheckBox();
        _logVerbosityLabel = new Label();
        _logVerbosityComboBox = new ComboBox();
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
        _migrateAccountBtn = new Button();
        _controlsButtonPanel = new TableLayoutPanel();
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
        _folderBrowserExePanel = new TableLayoutPanel();
        _folderBrowserArgsPanel = new TableLayoutPanel();
        _desktopSettingsPathPanel = new TableLayoutPanel();

        ((ISupportInitialize)_idleTimeoutUpDown).BeginInit();
        ((ISupportInitialize)_autoLockTimeoutUpDown).BeginInit();
        _startupGroup.SuspendLayout();
        _controlsButtonPanel.SuspendLayout();
        _pinGroup.SuspendLayout();
        _startupPinContainer.SuspendLayout();
        _autoLockGroup.SuspendLayout();
        _firewallGroup.SuspendLayout();
        _dragBridgeFirewallContainer.SuspendLayout();
        _autoLockDragBridgeContainer.SuspendLayout();
        _idleTimeoutGroup.SuspendLayout();
        _contextMenuGroup.SuspendLayout();
        _idleCtxContainer.SuspendLayout();
        _folderBrowserExePanel.SuspendLayout();
        _folderBrowserArgsPanel.SuspendLayout();
        _desktopSettingsPathPanel.SuspendLayout();
        _folderBrowserGroup.SuspendLayout();
        _desktopSettingsGroup.SuspendLayout();
        _folderSettingsContainer.SuspendLayout();
        _mainFillPanel.SuspendLayout();
        SuspendLayout();

        // --- Startup group ---
        _startupGroup.Text = "Startup";
        _startupGroup.Dock = DockStyle.Fill;

        _autoStartCheckBox.Text = "Auto-start on login";
        _autoStartCheckBox.Location = new Point(15, 25);
        _autoStartCheckBox.AutoSize = true;
        _startupGroup.Controls.Add(_autoStartCheckBox);

        _startWithoutPinCheckBox.Text = "Remember PIN";
        _startWithoutPinCheckBox.Location = new Point(15, 48);
        _startWithoutPinCheckBox.AutoSize = true;
        _startupGroup.Controls.Add(_startWithoutPinCheckBox);

        _logVerbosityLabel.Text = "Log verbosity:";
        _logVerbosityLabel.Location = new Point(200, 27);
        _logVerbosityLabel.AutoSize = true;
        _startupGroup.Controls.Add(_logVerbosityLabel);

        _logVerbosityComboBox.Location = new Point(292, 23);
        _logVerbosityComboBox.Width = 88;
        _logVerbosityComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _startupGroup.Controls.Add(_logVerbosityComboBox);

        _openLogButton.Text = "Open Log";
        _openLogButton.Location = new Point(390, 22);
        _openLogButton.Size = new Size(80, 27);
        _openLogButton.FlatStyle = FlatStyle.System;
        _openLogButton.Click += OnOpenLogClick;
        _startupGroup.Controls.Add(_openLogButton);

        // --- Controls group ---
        _pinGroup.Text = "Controls";
        _pinGroup.Dock = DockStyle.Fill;

        _changePinBtn.Text = "Change PIN";
        _changePinBtn.Dock = DockStyle.Fill;
        _changePinBtn.Margin = new Padding(3, 0, 3, 0);
        _changePinBtn.FlatStyle = FlatStyle.Standard;
        _changePinBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _changePinBtn.TextAlign = ContentAlignment.MiddleLeft;
        _changePinBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _changePinBtn.Padding = new Padding(4, 0, 0, 0);
        _changePinBtn.Click += OnChangePinClick;

        _securityCheckBtn.Text = "Security Check";
        _securityCheckBtn.Dock = DockStyle.Fill;
        _securityCheckBtn.Margin = new Padding(3, 0, 3, 0);
        _securityCheckBtn.FlatStyle = FlatStyle.Standard;
        _securityCheckBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _securityCheckBtn.TextAlign = ContentAlignment.MiddleLeft;
        _securityCheckBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _securityCheckBtn.Padding = new Padding(4, 0, 0, 0);
        _securityCheckBtn.Click += OnSecurityCheckClick;

        _cleanupBtn.Text = "Cleanup && Exit";
        _cleanupBtn.Dock = DockStyle.Fill;
        _cleanupBtn.Margin = new Padding(3, 0, 3, 0);
        _cleanupBtn.FlatStyle = FlatStyle.Standard;
        _cleanupBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _cleanupBtn.TextAlign = ContentAlignment.MiddleLeft;
        _cleanupBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _cleanupBtn.Padding = new Padding(4, 0, 0, 0);
        _cleanupBtn.Click += OnCleanupClick;

        _migrateAccountBtn.Text = "Migrate To";
        _migrateAccountBtn.Dock = DockStyle.Fill;
        _migrateAccountBtn.Margin = new Padding(3, 0, 3, 0);
        _migrateAccountBtn.FlatStyle = FlatStyle.Standard;
        _migrateAccountBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
        _migrateAccountBtn.TextAlign = ContentAlignment.MiddleLeft;
        _migrateAccountBtn.ImageAlign = ContentAlignment.MiddleLeft;
        _migrateAccountBtn.Padding = new Padding(4, 0, 0, 0);
        _migrateAccountBtn.Click += OnMigrateAccountClick;

        _controlsButtonPanel.Dock = DockStyle.Top;
        _controlsButtonPanel.Height = 38;
        _controlsButtonPanel.Font = new Font(Control.DefaultFont.FontFamily, 8f);
        _controlsButtonPanel.ColumnCount = 4;
        _controlsButtonPanel.RowCount = 1;
        _controlsButtonPanel.Margin = Padding.Empty;
        _controlsButtonPanel.Padding = new Padding(4, 2, 4, 4);
        _controlsButtonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _controlsButtonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _controlsButtonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _controlsButtonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _controlsButtonPanel.Controls.Add(_changePinBtn, 0, 0);
        _controlsButtonPanel.Controls.Add(_securityCheckBtn, 1, 0);
        _controlsButtonPanel.Controls.Add(_cleanupBtn, 2, 0);
        _controlsButtonPanel.Controls.Add(_migrateAccountBtn, 3, 0);

        _pinGroup.Controls.Add(_controlsButtonPanel);

        _startupPinContainer.Dock = DockStyle.Top;
        _startupPinContainer.Height = 72;
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

        _autoLockCheckBox.Text = "Lock in background";
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
        _autoLockTimeoutUpDown.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _autoLockGroup.Controls.Add(_autoLockTimeoutUpDown);

        _unlockModeComboBox.Location = new Point(348, 22);
        _unlockModeComboBox.Width = 115;
        _unlockModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _unlockModeComboBox.Items.AddRange(new object[] { "UAC (Admin)", "PIN", "UAC + PIN", "Windows Hello" });
        _unlockModeComboBox.SelectedIndex = 0;
        _unlockModeComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _autoLockGroup.Controls.Add(_unlockModeComboBox);

        // --- Firewall group ---
        _firewallGroup.Text = "Firewall";
        _firewallGroup.Dock = DockStyle.Fill;

        _blockIcmpCheckBox.Text = "Block ICMP when Internet is blocked";
        _blockIcmpCheckBox.Location = new Point(15, 24);
        _blockIcmpCheckBox.AutoSize = true;
        _firewallGroup.Controls.Add(_blockIcmpCheckBox);

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
        _folderBrowserGroup.Padding = new Padding(15, 0, 10, 5);

        _folderBrowserExeLabel.Text = "Application used to open folder entries:";
        _folderBrowserExeLabel.Dock = DockStyle.Top;
        _folderBrowserExeLabel.AutoSize = true;
        _folderBrowserExeLabel.Padding = new Padding(0, 4, 0, 2);

        _folderBrowserExeTextBox.Dock = DockStyle.Fill;

        _folderBrowseButton.Text = "Browse...";
        _folderBrowseButton.Dock = DockStyle.Fill;
        _folderBrowseButton.Height = _folderBrowserExeTextBox.PreferredHeight;
        _folderBrowseButton.FlatStyle = FlatStyle.System;
        _folderBrowseButton.Click += OnFolderBrowserBrowseClick;

        _folderBrowserExePanel.Dock = DockStyle.Top;
        _folderBrowserExePanel.Height = _folderBrowserExeTextBox.PreferredHeight;
        _folderBrowserExePanel.Margin = Padding.Empty;
        _folderBrowserExePanel.Padding = Padding.Empty;
        _folderBrowserExePanel.ColumnCount = 2;
        _folderBrowserExePanel.RowCount = 1;
        _folderBrowserExePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _folderBrowserExePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        _folderBrowserExePanel.Controls.Add(_folderBrowserExeTextBox, 0, 0);
        _folderBrowserExePanel.Controls.Add(_folderBrowseButton, 1, 0);

        _folderBrowserArgsLabel.Text = "Arguments (%1 = folder path):";
        _folderBrowserArgsLabel.AutoSize = true;
        _folderBrowserArgsLabel.TextAlign = ContentAlignment.MiddleLeft;

        _folderBrowserArgsTextBox.Dock = DockStyle.Fill;

        _folderBrowserArgsPanel.Dock = DockStyle.Top;
        _folderBrowserArgsPanel.Height = _folderBrowserArgsTextBox.PreferredHeight + 5;
        _folderBrowserArgsPanel.Margin = Padding.Empty;
        _folderBrowserArgsPanel.Padding = new Padding(0, 5, 0, 0);
        _folderBrowserArgsPanel.ColumnCount = 2;
        _folderBrowserArgsPanel.RowCount = 1;
        _folderBrowserArgsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _folderBrowserArgsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _folderBrowserArgsPanel.Controls.Add(_folderBrowserArgsLabel, 0, 0);
        _folderBrowserArgsPanel.Controls.Add(_folderBrowserArgsTextBox, 1, 0);

        // Add in reverse order (last added = topmost for DockStyle.Top):
        _folderBrowserGroup.Controls.Add(_folderBrowserArgsPanel);
        _folderBrowserGroup.Controls.Add(_folderBrowserExePanel);
        _folderBrowserGroup.Controls.Add(_folderBrowserExeLabel);

        // --- Desktop Settings group ---
        _desktopSettingsGroup.Text = "Desktop Settings";
        _desktopSettingsGroup.Dock = DockStyle.Fill;
        _desktopSettingsGroup.Padding = new Padding(15, 0, 10, 5);

        _settingsPathLabel.Text = "Default file for new account settings import:";
        _settingsPathLabel.Dock = DockStyle.Top;
        _settingsPathLabel.AutoSize = true;
        _settingsPathLabel.Padding = new Padding(0, 4, 0, 2);

        _defaultSettingsPathTextBox.Dock = DockStyle.Fill;

        _settingsBrowseButton.Text = "Browse...";
        _settingsBrowseButton.Dock = DockStyle.Fill;
        _settingsBrowseButton.Height = _defaultSettingsPathTextBox.PreferredHeight;
        _settingsBrowseButton.FlatStyle = FlatStyle.System;
        _settingsBrowseButton.Click += OnDefaultSettingsPathBrowseClick;

        _desktopSettingsPathPanel.Dock = DockStyle.Top;
        _desktopSettingsPathPanel.Height = _defaultSettingsPathTextBox.PreferredHeight + 5;
        _desktopSettingsPathPanel.Margin = Padding.Empty;
        _desktopSettingsPathPanel.Padding = new Padding(0, 0, 0, 5);
        _desktopSettingsPathPanel.ColumnCount = 2;
        _desktopSettingsPathPanel.RowCount = 1;
        _desktopSettingsPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _desktopSettingsPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        _desktopSettingsPathPanel.Controls.Add(_defaultSettingsPathTextBox, 0, 0);
        _desktopSettingsPathPanel.Controls.Add(_settingsBrowseButton, 1, 0);

        _exportSettingsButton.Text = "Export Current Desktop Settings As...";
        _exportSettingsButton.Dock = DockStyle.Top;
        _exportSettingsButton.Height = 28;
        _exportSettingsButton.FlatStyle = FlatStyle.System;
        _exportSettingsButton.Click += OnExportDesktopSettingsClick;

        // Add in reverse order (last added = topmost for DockStyle.Top):
        _desktopSettingsGroup.Controls.Add(_exportSettingsButton);
        _desktopSettingsGroup.Controls.Add(_desktopSettingsPathPanel);
        _desktopSettingsGroup.Controls.Add(_settingsPathLabel);

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

        ((ISupportInitialize)_idleTimeoutUpDown).EndInit();
        ((ISupportInitialize)_autoLockTimeoutUpDown).EndInit();
        _startupGroup.ResumeLayout(false);
        _controlsButtonPanel.ResumeLayout(false);
        _controlsButtonPanel.PerformLayout();
        _pinGroup.ResumeLayout(false);
        _startupPinContainer.ResumeLayout(false);
        _startupPinContainer.PerformLayout();
        _autoLockGroup.ResumeLayout(false);
        _firewallGroup.ResumeLayout(false);
        _dragBridgeFirewallContainer.ResumeLayout(false);
        _dragBridgeFirewallContainer.PerformLayout();
        _autoLockDragBridgeContainer.ResumeLayout(false);
        _autoLockDragBridgeContainer.PerformLayout();
        _idleTimeoutGroup.ResumeLayout(false);
        _contextMenuGroup.ResumeLayout(false);
        _idleCtxContainer.ResumeLayout(false);
        _idleCtxContainer.PerformLayout();
        _folderBrowserExePanel.ResumeLayout(false);
        _folderBrowserExePanel.PerformLayout();
        _folderBrowserArgsPanel.ResumeLayout(false);
        _folderBrowserArgsPanel.PerformLayout();
        _desktopSettingsPathPanel.ResumeLayout(false);
        _desktopSettingsPathPanel.PerformLayout();
        _folderBrowserGroup.ResumeLayout(false);
        _folderBrowserGroup.PerformLayout();
        _desktopSettingsGroup.ResumeLayout(false);
        _desktopSettingsGroup.PerformLayout();
        _folderSettingsContainer.ResumeLayout(false);
        _folderSettingsContainer.PerformLayout();
        _mainFillPanel.ResumeLayout(false);
        _mainFillPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}

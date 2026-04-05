#nullable disable
using System.ComponentModel;

namespace RunFence.Account.UI.AppContainer;

partial class AppContainerEditDialog
{
    private IContainer components = null;

    private Panel _buttonStrip;
    private TableLayoutPanel _layout;
    private Label _displayNameLabel;
    private TextBox _displayNameBox;
    private Label _profileNameLabel;
    private TextBox _profileNameBox;
    private Label _sidLabel;
    private TextBox _sidBox;
    private GroupBox _capGroupBox;
    private FlowLayoutPanel _capFlow;
    private CheckBox _loopbackCheckBox;
    private CheckBox[] _capCheckBoxes; // filled from KnownCapabilities in InitializeCapabilities
    private GroupBox _comGroupBox;
    private Panel _clsidPanel;
    private ToolStrip _comToolStrip;   // items added in SetupComToolbar — images require UiIconFactory.CreateToolbarIcon
    private ListBox _comCustomListBox;
    private CheckBox _ephemeralCheckBox;
    private FlowLayoutPanel _buttonPanel;
    private Button _okButton;
    private Button _cancelButton;
    private Button _deleteButton;
    private ToolTip _toolTip;

    private AppContainerEditDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        _buttonStrip = new Panel();
        _layout = new TableLayoutPanel();
        _displayNameLabel = new Label();
        _displayNameBox = new TextBox();
        _profileNameLabel = new Label();
        _profileNameBox = new TextBox();
        _sidLabel = new Label();
        _sidBox = new TextBox();
        _capGroupBox = new GroupBox();
        _capFlow = new FlowLayoutPanel();
        _loopbackCheckBox = new CheckBox();
        _comGroupBox = new GroupBox();
        _clsidPanel = new Panel();
        _comToolStrip = new ToolStrip();
        _comCustomListBox = new ListBox();
        _ephemeralCheckBox = new CheckBox();
        _buttonPanel = new FlowLayoutPanel();
        _okButton = new Button();
        _cancelButton = new Button();
        _deleteButton = new Button();
        _toolTip = new ToolTip(components);

        SuspendLayout();

        // _layout
        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 2;
        _layout.Padding = new Padding(0);
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        _layout.RowCount = 6;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 0: Display Name
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 1: Profile Name
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 2: SID (edit only, hidden in create)
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 3: Capabilities
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 4: COM Access
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 5: Ephemeral (create only, hidden in edit)

        // _displayNameLabel
        _displayNameLabel.Text = "Display Name:";
        _displayNameLabel.AutoSize = false;
        _displayNameLabel.Height = 26;
        _displayNameLabel.Margin = new Padding(0, 4, 8, 4);
        _displayNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _displayNameLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        // _profileNameLabel
        _profileNameLabel.Text = "Profile Name:";
        _profileNameLabel.AutoSize = false;
        _profileNameLabel.Height = 26;
        _profileNameLabel.Margin = new Padding(0, 4, 8, 4);
        _profileNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _profileNameLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        // _sidLabel
        _sidLabel.Text = "Container SID:";
        _sidLabel.AutoSize = false;
        _sidLabel.Height = 26;
        _sidLabel.Margin = new Padding(0, 4, 8, 4);
        _sidLabel.TextAlign = ContentAlignment.MiddleLeft;
        _sidLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        // _displayNameBox
        _displayNameBox.Margin = new Padding(0, 4, 0, 4);
        _displayNameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _displayNameBox.TextChanged += OnDisplayNameChanged;

        // _profileNameBox (ReadOnly and BackColor set in ConfigureMode)
        _profileNameBox.Margin = new Padding(0, 4, 0, 4);
        _profileNameBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        // _sidBox
        _sidBox.ReadOnly = true;
        _sidBox.BackColor = SystemColors.Control;
        _sidBox.Margin = new Padding(0, 4, 0, 4);
        _sidBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        // _capGroupBox
        _capGroupBox.Text = "Capabilities";
        _capGroupBox.AutoSize = true;
        _capGroupBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _capGroupBox.Margin = new Padding(0, 8, 0, 4);
        _capGroupBox.Padding = new Padding(0, 0, 8, 8);

        // _capFlow (checkboxes and _loopbackCheckBox added in InitializeCapabilities)
        _capFlow.FlowDirection = FlowDirection.LeftToRight;
        _capFlow.WrapContents = true;
        _capFlow.MaximumSize = new Size(424, 0);
        _capFlow.AutoSize = true;
        _capFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _capFlow.Padding = new Padding(2);
        _capFlow.Margin = new Padding(0);
        _capFlow.Location = new Point(8, 22);   // below GroupBox title

        // _loopbackCheckBox
        _loopbackCheckBox.Text = "Enable localhost access (loopback exemption)";
        _loopbackCheckBox.AutoSize = false;
        _loopbackCheckBox.Width = 414;
        _loopbackCheckBox.Margin = new Padding(2, 6, 2, 2);

        // _comGroupBox — AutoSize row; Anchor+explicit Height replaces Dock=Fill
        _comGroupBox.Text = "COM Access (Shell / WScript / COM objects)";
        _comGroupBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _comGroupBox.Height = 160;
        _comGroupBox.MinimumSize = new Size(0, 120);
        _comGroupBox.Margin = new Padding(0, 6, 0, 4);
        _comGroupBox.Padding = new Padding(8, 4, 8, 8);

        // _clsidPanel
        _clsidPanel.Dock = DockStyle.Fill;

        // _comToolStrip
        _comToolStrip.Dock = DockStyle.Top;
        _comToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _comToolStrip.BackColor = SystemColors.Control;
        _comToolStrip.RenderMode = ToolStripRenderMode.System;
        _comToolStrip.Padding = new Padding(2, 0, 0, 0);

        // _comCustomListBox
        _comCustomListBox.Dock = DockStyle.Fill;
        _comCustomListBox.IntegralHeight = false;
        _comCustomListBox.Height = 60;

        // _ephemeralCheckBox
        _ephemeralCheckBox.Text = "Ephemeral — auto-delete after 24 hours";
        _ephemeralCheckBox.AutoSize = true;
        _ephemeralCheckBox.Margin = new Padding(2, 0, 0, 4);
        _ephemeralCheckBox.CheckedChanged += OnEphemeralChanged;

        // _buttonPanel — right-docked inside _buttonStrip; Size.Width=168 is the dock width (80+8+80)
        _buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        _buttonPanel.Dock = DockStyle.Right;
        _buttonPanel.Size = new Size(168, 28);

        // _okButton
        _okButton.Text = "OK";
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Width = 80;
        _okButton.Height = 28;
        _okButton.Margin = new Padding(0);
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Width = 80;
        _cancelButton.Height = 28;
        _cancelButton.Margin = new Padding(8, 0, 0, 0); // 8px gap between OK and Cancel in RightToLeft flow

        // _deleteButton — left-docked, visible in edit mode only (set in ConfigureMode)
        _deleteButton.Text = "Delete Container";
        _deleteButton.Size = new Size(120, 28);
        _deleteButton.Dock = DockStyle.Left;
        _deleteButton.Click += OnDeleteClick;

        // _buttonStrip — bottom-docked panel, outside _layout; eliminates column-split button bug
        // Padding(0,8,0,8) centers the 28px-tall buttons vertically in the 44px strip
        _buttonStrip.Dock = DockStyle.Bottom;
        _buttonStrip.Height = 44;
        _buttonStrip.Padding = new Padding(0, 8, 0, 8);

        // Layout: _layout rows
        _layout.Controls.Add(_displayNameLabel, 0, 0);
        _layout.Controls.Add(_displayNameBox, 1, 0);
        _layout.Controls.Add(_profileNameLabel, 0, 1);
        _layout.Controls.Add(_profileNameBox, 1, 1);
        _layout.Controls.Add(_sidLabel, 0, 2);
        _layout.Controls.Add(_sidBox, 1, 2);
        _layout.Controls.Add(_capGroupBox, 0, 3);
        _layout.SetColumnSpan(_capGroupBox, 2);
        _layout.Controls.Add(_comGroupBox, 0, 4);
        _layout.SetColumnSpan(_comGroupBox, 2);
        _layout.Controls.Add(_ephemeralCheckBox, 0, 5);
        _layout.SetColumnSpan(_ephemeralCheckBox, 2);

        // Assemble containers
        _capGroupBox.Controls.Add(_capFlow);
        _clsidPanel.Controls.Add(_comCustomListBox);
        _clsidPanel.Controls.Add(_comToolStrip);
        _comGroupBox.Controls.Add(_clsidPanel);
        _buttonPanel.Controls.Add(_cancelButton);
        _buttonPanel.Controls.Add(_okButton);
        _buttonStrip.Controls.Add(_buttonPanel);
        _buttonStrip.Controls.Add(_deleteButton);

        // Form
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(492, 610);
        Padding = new Padding(16);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_layout);
        Controls.Add(_buttonStrip);

        ResumeLayout(false);
        PerformLayout();
    }
}

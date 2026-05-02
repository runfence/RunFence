#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class HandlerMappingAddDialog
{
    private IContainer components = null;

    private TableLayoutPanel _layout;
    private ToolStrip _modeToolStrip;
    private ToolStripControlHost _modeRadioHost;
    private FlowLayoutPanel _radioPanel;
    private RadioButton _radioApp;
    private RadioButton _radioDirect;
    private Label _appLabel;
    private ComboBox _appCombo;
    private Label _handlerLabel;
    private TextBox _handlerTextBox;
    private Label _keyLabel;
    private ComboBox _keyCombo;
    private Label _templateLabel;
    private TextBox _templateTextBox;
    private CombinedPrefixesSection _combinedPrefixesSection;
    private FlowLayoutPanel _buttonPanel;
    private Button _okButton;
    private Button _cancelButton;

    private HandlerMappingAddDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _layout = new TableLayoutPanel();
        _modeToolStrip = new ToolStrip();
        _radioPanel = new FlowLayoutPanel();
        _radioApp = new RadioButton();
        _radioDirect = new RadioButton();
        _modeRadioHost = new ToolStripControlHost(_radioPanel);
        _appLabel = new Label();
        _appCombo = new ComboBox();
        _handlerLabel = new Label();
        _handlerTextBox = new TextBox();
        _keyLabel = new Label();
        _keyCombo = new ComboBox();
        _templateLabel = new Label();
        _templateTextBox = new TextBox();
        _combinedPrefixesSection = new CombinedPrefixesSection();
        _buttonPanel = new FlowLayoutPanel();
        _okButton = new Button();
        _cancelButton = new Button();

        _layout.SuspendLayout();
        _buttonPanel.SuspendLayout();
        _radioPanel.SuspendLayout();
        SuspendLayout();

        // Form
        Text = "Add Handler Association";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(370, 580);
        Padding = new Padding(12, 8, 12, 8);

        // _layout — TableLayoutPanel filling form area above the button panel
        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 10;
        _layout.Padding = new Padding(0);
        _layout.Margin = new Padding(0);
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));   // 0: mode toolstrip
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));   // 1: app label (app mode)
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));   // 2: app combo (app mode)
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));    // 3: handler label (direct mode, initially hidden)
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));    // 4: handler textbox (direct mode, initially hidden)
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));   // 5: key label
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));   // 6: key combo
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));   // 7: template label (app mode)
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));   // 8: template textbox (app mode)
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 9: combined prefixes (app mode, Fill)

        // _modeToolStrip
        _modeToolStrip.Dock = DockStyle.Fill;
        _modeToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _modeToolStrip.RenderMode = ToolStripRenderMode.System;
        _modeToolStrip.ImageScalingSize = new Size(24, 24);
        _modeToolStrip.Items.Add(_modeRadioHost);

        // _modeRadioHost
        _modeRadioHost.AutoSize = true;
        _modeRadioHost.Margin = new Padding(0);
        _modeRadioHost.Padding = Padding.Empty;

        // _radioPanel
        _radioPanel.FlowDirection = FlowDirection.LeftToRight;
        _radioPanel.WrapContents = false;
        _radioPanel.AutoSize = true;
        _radioPanel.Padding = new Padding(0);
        _radioPanel.Margin = Padding.Empty;

        // _radioApp
        _radioApp.Text = "Application";
        _radioApp.AutoSize = true;
        _radioApp.Checked = true;
        _radioApp.Margin = new Padding(0, 3, 12, 0);
        _radioApp.CheckedChanged += OnRadioAppCheckedChanged;

        // _radioDirect
        _radioDirect.Text = "Direct handler";
        _radioDirect.AutoSize = true;
        _radioDirect.Margin = new Padding(0, 3, 0, 0);
        _radioDirect.CheckedChanged += OnRadioDirectCheckedChanged;

        _radioPanel.Controls.Add(_radioApp);
        _radioPanel.Controls.Add(_radioDirect);

        // _appLabel
        _appLabel.Text = "Application:";
        _appLabel.Dock = DockStyle.Top;
        _appLabel.AutoSize = true;
        _appLabel.Margin = new Padding(0);

        // _appCombo
        _appCombo.Dock = DockStyle.Top;
        _appCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _appCombo.Margin = new Padding(0, 0, 0, 4);
        _appCombo.SelectedIndexChanged += OnAppComboSelectedIndexChanged;

        // _handlerLabel
        _handlerLabel.Text = "Handler (class name or command):";
        _handlerLabel.Dock = DockStyle.Top;
        _handlerLabel.AutoSize = true;
        _handlerLabel.Visible = false;
        _handlerLabel.Margin = new Padding(0);

        // _handlerTextBox
        _handlerTextBox.Dock = DockStyle.Top;
        _handlerTextBox.Visible = false;
        _handlerTextBox.Margin = new Padding(0, 0, 0, 4);
        _handlerTextBox.TextChanged += OnHandlerTextChanged;

        // _keyLabel
        _keyLabel.Text = "Extension or Protocol:";
        _keyLabel.Dock = DockStyle.Top;
        _keyLabel.AutoSize = true;
        _keyLabel.Margin = new Padding(0);

        // _keyCombo
        _keyCombo.Dock = DockStyle.Top;
        _keyCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _keyCombo.Margin = new Padding(0, 0, 0, 4);
        _keyCombo.TextChanged += OnKeyComboTextChanged;

        // _templateLabel
        _templateLabel.Text = "Parameters Template:";
        _templateLabel.Dock = DockStyle.Top;
        _templateLabel.AutoSize = true;
        _templateLabel.Margin = new Padding(0);

        // _templateTextBox
        _templateTextBox.Dock = DockStyle.Top;
        _templateTextBox.Text = "\"%1\"";
        _templateTextBox.Margin = new Padding(0, 0, 0, 4);

        // _combinedPrefixesSection
        _combinedPrefixesSection.Dock = DockStyle.Fill;
        _combinedPrefixesSection.Margin = new Padding(0, 0, 0, 4);

        // _buttonPanel — docked to bottom of form, outside the TableLayoutPanel
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        _buttonPanel.WrapContents = false;
        _buttonPanel.AutoSize = true;
        _buttonPanel.Padding = new Padding(0, 4, 0, 0);

        // _okButton
        _okButton.Text = "OK";
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Size = new Size(80, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Margin = new Padding(4, 0, 0, 0);

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Size = new Size(80, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Margin = new Padding(4, 0, 0, 0);

        _buttonPanel.Controls.Add(_cancelButton);
        _buttonPanel.Controls.Add(_okButton);

        _layout.Controls.Add(_modeToolStrip, 0, 0);
        _layout.Controls.Add(_appLabel, 0, 1);
        _layout.Controls.Add(_appCombo, 0, 2);
        _layout.Controls.Add(_handlerLabel, 0, 3);
        _layout.Controls.Add(_handlerTextBox, 0, 4);
        _layout.Controls.Add(_keyLabel, 0, 5);
        _layout.Controls.Add(_keyCombo, 0, 6);
        _layout.Controls.Add(_templateLabel, 0, 7);
        _layout.Controls.Add(_templateTextBox, 0, 8);
        _layout.Controls.Add(_combinedPrefixesSection, 0, 9);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.Add(_buttonPanel);
        Controls.Add(_layout);

        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _buttonPanel.ResumeLayout(false);
        _buttonPanel.PerformLayout();
        _radioPanel.ResumeLayout(false);
        _radioPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}

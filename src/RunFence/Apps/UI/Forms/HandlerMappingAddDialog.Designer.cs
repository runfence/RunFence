#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class HandlerMappingAddDialog
{
    private IContainer components = null;

    private RadioButton _radioApp;
    private RadioButton _radioDirect;
    private Label _keyLabel;
    private ComboBox _keyCombo;
    private Label _appLabel;
    private ComboBox _appCombo;
    private Label _handlerLabel;
    private TextBox _handlerTextBox;
    private Label _templateLabel;
    private TextBox _templateTextBox;
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
        _radioApp = new RadioButton();
        _radioDirect = new RadioButton();
        _keyLabel = new Label();
        _keyCombo = new ComboBox();
        _appLabel = new Label();
        _appCombo = new ComboBox();
        _handlerLabel = new Label();
        _handlerTextBox = new TextBox();
        _templateLabel = new Label();
        _templateTextBox = new TextBox();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // Form
        Text = "Add Handler Association";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(370, 235);

        // _radioApp
        _radioApp.Text = "Application";
        _radioApp.Location = new Point(15, 12);
        _radioApp.AutoSize = true;
        _radioApp.Checked = true;
        _radioApp.CheckedChanged += OnRadioAppCheckedChanged;

        // _radioDirect
        _radioDirect.Text = "Direct handler";
        _radioDirect.Location = new Point(130, 12);
        _radioDirect.AutoSize = true;
        _radioDirect.CheckedChanged += OnRadioDirectCheckedChanged;

        // _keyLabel
        _keyLabel.Text = "Extension or Protocol:";
        _keyLabel.Location = new Point(15, 42);
        _keyLabel.AutoSize = true;

        // _keyCombo
        _keyCombo.Location = new Point(15, 62);
        _keyCombo.Size = new Size(340, 23);
        _keyCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _keyCombo.TextChanged += OnKeyComboTextChanged;

        // _appLabel
        _appLabel.Text = "Application:";
        _appLabel.Location = new Point(15, 95);
        _appLabel.AutoSize = true;

        // _appCombo
        _appCombo.Location = new Point(15, 115);
        _appCombo.Size = new Size(340, 23);
        _appCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _appCombo.SelectedIndexChanged += OnAppComboSelectedIndexChanged;

        // _handlerLabel
        _handlerLabel.Text = "Handler (class name or command):";
        _handlerLabel.Location = new Point(15, 95);
        _handlerLabel.AutoSize = true;
        _handlerLabel.Visible = false;

        // _handlerTextBox
        _handlerTextBox.Location = new Point(15, 115);
        _handlerTextBox.Size = new Size(340, 23);
        _handlerTextBox.Visible = false;

        // _templateLabel
        _templateLabel.Text = "Parameters Template:";
        _templateLabel.Location = new Point(15, 148);
        _templateLabel.AutoSize = true;

        // _templateTextBox
        _templateTextBox.Location = new Point(15, 165);
        _templateTextBox.Size = new Size(340, 23);
        _templateTextBox.Text = "\"%1\"";

        // _okButton
        _okButton.Text = "OK";
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Location = new Point(195, 200);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(280, 200);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.Add(_radioApp);
        Controls.Add(_radioDirect);
        Controls.Add(_keyLabel);
        Controls.Add(_keyCombo);
        Controls.Add(_appLabel);
        Controls.Add(_appCombo);
        Controls.Add(_handlerLabel);
        Controls.Add(_handlerTextBox);
        Controls.Add(_templateLabel);
        Controls.Add(_templateTextBox);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        ResumeLayout(false);
        PerformLayout();
    }
}

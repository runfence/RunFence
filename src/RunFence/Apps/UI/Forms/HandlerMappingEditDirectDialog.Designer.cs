#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class HandlerMappingEditDirectDialog
{
    private IContainer components = null;

    private Label _label;
    private TextBox _textBox;
    private Button _okButton;
    private Button _cancelButton;

    public HandlerMappingEditDirectDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _label = new Label();
        _textBox = new TextBox();
        _okButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // Form
        Text = "Edit Direct Handler";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(370, 100);

        // _label
        _label.Text = "Handler (class name or command):";
        _label.Location = new Point(15, 12);
        _label.AutoSize = true;

        // _textBox
        _textBox.Location = new Point(15, 35);
        _textBox.Size = new Size(340, 23);
        _textBox.TextChanged += OnTextBoxTextChanged;

        // _okButton
        _okButton.Text = "OK";
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Location = new Point(195, 65);
        _okButton.Size = new Size(75, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(280, 65);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.Add(_label);
        Controls.Add(_textBox);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        ResumeLayout(false);
        PerformLayout();
    }
}

#nullable disable

using System.ComponentModel;

namespace RunFence.Licensing.UI.Forms;

partial class LicenseActivationDialog
{
    private IContainer components = null;

    private Label _instructionLabel;
    private TextBox _keyTextBox;
    private Button _okButton;
    private Button _cancelButton;
    private TableLayoutPanel _buttonPanel;

    private LicenseActivationDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _instructionLabel = new Label();
        _keyTextBox = new TextBox();
        _okButton = new Button();
        _cancelButton = new Button();
        _buttonPanel = new TableLayoutPanel();

        SuspendLayout();
        _buttonPanel.SuspendLayout();

        // _instructionLabel
        _instructionLabel.Text = "Enter your license key:";
        _instructionLabel.AutoSize = true;
        _instructionLabel.Font = new Font("Segoe UI", 9f);
        _instructionLabel.Location = new Point(12, 12);

        // _keyTextBox — multiline for the ~140-char key
        _keyTextBox.Multiline = true;
        _keyTextBox.WordWrap = true;
        _keyTextBox.ScrollBars = ScrollBars.None;
        _keyTextBox.Font = new Font("Consolas", 9f);
        _keyTextBox.Location = new Point(12, 34);
        _keyTextBox.Size = new Size(460, 70);
        _keyTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _keyTextBox.AcceptsReturn = false;

        // _buttonPanel
        _buttonPanel.ColumnCount = 2;
        _buttonPanel.RowCount = 1;
        _buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _buttonPanel.Location = new Point(12, 116);
        _buttonPanel.Size = new Size(460, 32);
        _buttonPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

        // _okButton
        _okButton.Text = "Activate";
        _okButton.Size = new Size(220, 28);
        _okButton.Anchor = AnchorStyles.Right;
        _okButton.Click += OnOkClick;
        _buttonPanel.Controls.Add(_okButton, 0, 0);

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(220, 28);
        _cancelButton.Anchor = AnchorStyles.Left;
        _cancelButton.Click += OnCancelClick;
        _buttonPanel.Controls.Add(_cancelButton, 1, 0);

        // Form
        Text = "Enter License Key";
        ClientSize = new Size(484, 160);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_instructionLabel);
        Controls.Add(_keyTextBox);
        Controls.Add(_buttonPanel);

        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

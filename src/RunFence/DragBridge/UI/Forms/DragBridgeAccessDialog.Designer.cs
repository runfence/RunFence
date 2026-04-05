#nullable disable

using System.ComponentModel;

namespace RunFence.DragBridge.UI.Forms;

partial class DragBridgeAccessDialog
{
    private IContainer components = null;

    private Label _headerLabel;
    private ListBox _fileListBox;
    private Label _sizeWarningLabel;
    private Button _grantButton;
    private Button _copyButton;
    private Button _copyWholeFolderButton;
    private Button _cancelButton;
    private ToolTip _tooltip;

    private DragBridgeAccessDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        _tooltip = new ToolTip(components);
        _headerLabel = new Label();
        _fileListBox = new ListBox();
        _sizeWarningLabel = new Label();
        _grantButton = new Button();
        _copyButton = new Button();
        _copyWholeFolderButton = new Button();
        _cancelButton = new Button();

        SuspendLayout();

        // _headerLabel (text set in code)
        _headerLabel.Location = new Point(12, 12);
        _headerLabel.Size = new Size(456, 36);
        _headerLabel.AutoSize = false;

        // _fileListBox
        _fileListBox.Location = new Point(12, 52);
        _fileListBox.Size = new Size(456, 120);

        // _sizeWarningLabel (shown only when size > threshold, Visible=false by default)
        _sizeWarningLabel.Location = new Point(12, 182);
        _sizeWarningLabel.Size = new Size(456, 20);
        _sizeWarningLabel.ForeColor = Color.DarkOrange;
        _sizeWarningLabel.Visible = false;

        // _grantButton (position set in code based on warning visibility)
        _grantButton.Text = "Grant Access";
        _grantButton.Location = new Point(12, 182);
        _grantButton.Size = new Size(130, 34);
        _grantButton.FlatStyle = FlatStyle.System;
        _grantButton.Click += OnGrantClick;
        _tooltip.SetToolTip(_grantButton, "Adds file system ACL entries to grant the target account read access to the files.");

        // _copyButton (position set in code)
        _copyButton.Text = "Copy to Temp";
        _copyButton.Location = new Point(152, 182);
        _copyButton.Size = new Size(130, 34);
        _copyButton.FlatStyle = FlatStyle.System;
        _copyButton.Click += OnCopyClick;
        _tooltip.SetToolTip(_copyButton, "Copies the inaccessible files to a shared temp folder accessible by the target account.");

        // _copyWholeFolderButton (position set in code)
        _copyWholeFolderButton.Text = "Whole Folder";
        _copyWholeFolderButton.Location = new Point(292, 182);
        _copyWholeFolderButton.Size = new Size(130, 34);
        _copyWholeFolderButton.FlatStyle = FlatStyle.System;
        _copyWholeFolderButton.Click += OnCopyWholeFolderClick;
        _tooltip.SetToolTip(_copyWholeFolderButton, "Copies the entire folder(s) containing the inaccessible files to a shared temp folder accessible by the target account.");

        // _cancelButton (position set in code)
        _cancelButton.Text = "Cancel";
        _cancelButton.Location = new Point(468, 182);
        _cancelButton.Size = new Size(90, 34);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Click += OnCancelClick;
        _tooltip.SetToolTip(_cancelButton, "Cancel the paste operation.");

        // DragBridgeAccessDialog
        Text = "File Access Required";
        Icon = SystemIcons.Warning;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(570, 228);
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[]
        {
            _headerLabel, _fileListBox, _sizeWarningLabel,
            _grantButton, _copyButton, _copyWholeFolderButton, _cancelButton
        });

        ResumeLayout(false);
        PerformLayout();
    }
}

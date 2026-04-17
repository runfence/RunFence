#nullable disable

using System.ComponentModel;

namespace RunFence.DragBridge.UI.Forms;

partial class DragBridgeAccessDialog
{
    private IContainer components = null;

    private Label _headerLabel;
    private ListBox _fileListBox;
    private Label _sizeWarningLabel;
    private TableLayoutPanel _buttonRow;
    private FlowLayoutPanel _actionButtonsPanel;
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
        _buttonRow = new TableLayoutPanel();
        _actionButtonsPanel = new FlowLayoutPanel();
        _grantButton = new Button();
        _copyButton = new Button();
        _copyWholeFolderButton = new Button();
        _cancelButton = new Button();

        _buttonRow.SuspendLayout();
        _actionButtonsPanel.SuspendLayout();
        SuspendLayout();

        // _headerLabel (text set in production constructor)
        _headerLabel.Dock = DockStyle.Top;
        _headerLabel.Padding = new Padding(0, 0, 0, 4);
        _headerLabel.AutoSize = false;
        _headerLabel.Height = 40;
        _headerLabel.Text = "The target account cannot access N file(s):";

        // _fileListBox
        _fileListBox.Dock = DockStyle.Top;
        _fileListBox.Height = 120;
        _fileListBox.IntegralHeight = false;

        // _sizeWarningLabel (shown only when size > threshold)
        _sizeWarningLabel.Dock = DockStyle.Top;
        _sizeWarningLabel.ForeColor = Color.DarkOrange;
        _sizeWarningLabel.AutoSize = false;
        _sizeWarningLabel.Height = 24;
        _sizeWarningLabel.Padding = new Padding(0, 4, 0, 0);
        _sizeWarningLabel.Visible = false;
        _sizeWarningLabel.Text = "Warning: Total size is N MB. Copying to Temp may take time.";

        // _grantButton
        _grantButton.Text = "Grant Access";
        _grantButton.Size = new Size(130, 28);
        _grantButton.FlatStyle = FlatStyle.Standard;
        _grantButton.Click += OnGrantClick;

        // _copyButton
        _copyButton.Text = "Copy to Temp";
        _copyButton.Size = new Size(130, 28);
        _copyButton.FlatStyle = FlatStyle.Standard;
        _copyButton.Click += OnCopyClick;

        // _copyWholeFolderButton
        _copyWholeFolderButton.Text = "Whole Folder";
        _copyWholeFolderButton.Size = new Size(130, 28);
        _copyWholeFolderButton.FlatStyle = FlatStyle.Standard;
        _copyWholeFolderButton.Click += OnCopyWholeFolderClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(90, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _cancelButton.Click += OnCancelClick;

        // _actionButtonsPanel — left-aligned group of action buttons
        _actionButtonsPanel.AutoSize = true;
        _actionButtonsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _actionButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
        _actionButtonsPanel.WrapContents = false;
        _actionButtonsPanel.Dock = DockStyle.Fill;
        _actionButtonsPanel.Controls.AddRange(new Control[] { _grantButton, _copyWholeFolderButton, _copyButton });

        // _buttonRow — two-column row: action buttons left, cancel right
        _buttonRow.Dock = DockStyle.Top;
        _buttonRow.AutoSize = true;
        _buttonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonRow.ColumnCount = 2;
        _buttonRow.RowCount = 1;
        _buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _buttonRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _buttonRow.Padding = new Padding(0, 8, 0, 0);
        _buttonRow.Controls.Add(_actionButtonsPanel, 0, 0);
        _buttonRow.Controls.Add(_cancelButton, 1, 0);

        // DragBridgeAccessDialog
        Text = "File Access Required";
        Icon = SystemIcons.Warning;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12, 12, 12, 12);
        MinimumSize = new Size(570, 0);
        CancelButton = _cancelButton;
        Controls.AddRange(new Control[]
        {
            _buttonRow, _sizeWarningLabel, _fileListBox, _headerLabel
        });

        _buttonRow.ResumeLayout(false);
        _buttonRow.PerformLayout();
        _actionButtonsPanel.ResumeLayout(false);
        _actionButtonsPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}

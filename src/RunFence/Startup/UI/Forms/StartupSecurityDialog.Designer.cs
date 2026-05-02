#nullable disable

using System.ComponentModel;

namespace RunFence.Startup.UI.Forms;

partial class StartupSecurityDialog
{
    private IContainer components = null;

    private Label _warningLabel;
    private Label _descLabel;
    private ListView _listView;
    private ColumnHeader _locationHeader;
    private ColumnHeader _principalHeader;
    private ColumnHeader _accessHeader;
    private Label _remediationLabel;
    private Button _openLocationButton;
    private Button _copyButton;
    private Button _dismissButton;
    private Panel _bottomPanel;
    private Panel _topPanel;

    private StartupSecurityDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _warningLabel = new Label();
        _descLabel = new Label();
        _listView = new ListView();
        _locationHeader = new ColumnHeader();
        _principalHeader = new ColumnHeader();
        _accessHeader = new ColumnHeader();
        _remediationLabel = new Label();
        _openLocationButton = new Button();
        _copyButton = new Button();
        _dismissButton = new Button();
        _bottomPanel = new Panel();
        _topPanel = new Panel();

        SuspendLayout();
        _topPanel.SuspendLayout();
        _bottomPanel.SuspendLayout();

        // _topPanel
        _topPanel.Dock = DockStyle.Top;
        _topPanel.Height = 90;
        _topPanel.Padding = new Padding(12, 10, 12, 0);
        _topPanel.Controls.Add(_warningLabel);
        _topPanel.Controls.Add(_descLabel);

        // _warningLabel
        _warningLabel.Text = "Writable startup locations detected";
        _warningLabel.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        _warningLabel.ForeColor = Color.FromArgb(0xCC, 0x77, 0x00);
        _warningLabel.Dock = DockStyle.Top;
        _warningLabel.AutoSize = false;
        _warningLabel.Height = 22;
        _warningLabel.Padding = new Padding(0, 0, 0, 4);

        // _descLabel
        _descLabel.Text = "The following startup locations are writable by non-administrator accounts. " +
                          "This is a potential privilege escalation vector \u2014 a standard account could " +
                          "place a program that runs automatically when an administrator logs in.";
        _descLabel.Dock = DockStyle.Bottom;
        _descLabel.AutoSize = false;
        _descLabel.Height = 46;

        // _bottomPanel
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Height = 44;
        _bottomPanel.Padding = new Padding(12, 8, 12, 8);
        _bottomPanel.Controls.Add(_dismissButton);
        _bottomPanel.Controls.Add(_copyButton);
        _bottomPanel.Controls.Add(_openLocationButton);
        _bottomPanel.Controls.Add(_remediationLabel);

        // _openLocationButton
        _openLocationButton.Text = "Open Location";
        _openLocationButton.Dock = DockStyle.Right;
        _openLocationButton.Size = new Size(130, 28);
        _openLocationButton.FlatStyle = FlatStyle.System;
        _openLocationButton.Enabled = false;
        _openLocationButton.Click += OnOpenLocationClick;

        // _copyButton
        _copyButton.Text = "Copy to Clipboard";
        _copyButton.Dock = DockStyle.Right;
        _copyButton.Size = new Size(140, 28);
        _copyButton.FlatStyle = FlatStyle.System;
        _copyButton.Click += OnCopyClick;

        // _dismissButton
        _dismissButton.Text = "Dismiss";
        _dismissButton.DialogResult = DialogResult.OK;
        _dismissButton.Dock = DockStyle.Right;
        _dismissButton.Size = new Size(80, 28);
        _dismissButton.FlatStyle = FlatStyle.System;

        // _remediationLabel
        _remediationLabel.Text = "Recommended fix: Remove this principal from the drive root ACL. " +
                                 "Then use allow-mode permissions on specific subfolders to grant only the access they actually need.";
        _remediationLabel.Dock = DockStyle.Fill;
        _remediationLabel.AutoSize = false;
        _remediationLabel.ForeColor = SystemColors.GrayText;
        _remediationLabel.Visible = false;

        // _listView
        _listView.Dock = DockStyle.Fill;
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.GridLines = true;
        _listView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _listView.ShowGroups = true;

        // _locationHeader
        _locationHeader.Text = "Location";
        _locationHeader.Width = 500;

        // _principalHeader
        _principalHeader.Text = "Principal";
        _principalHeader.Width = 240;

        // _accessHeader
        _accessHeader.Text = "Access";
        _accessHeader.Width = 200;

        _listView.Columns.AddRange(new ColumnHeader[] { _locationHeader, _principalHeader, _accessHeader });

        // StartupSecurityDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Startup Security Warning";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 742);
        MinimumSize = new Size(700, 500);
        Padding = new Padding(12);
        AcceptButton = _dismissButton;
        CancelButton = _dismissButton;
        Controls.Add(_listView);
        Controls.Add(_bottomPanel);
        Controls.Add(_topPanel);

        _topPanel.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

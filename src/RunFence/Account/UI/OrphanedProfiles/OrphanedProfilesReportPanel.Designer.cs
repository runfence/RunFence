#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.OrphanedProfiles;

partial class OrphanedProfilesReportPanel
{
    private IContainer components = null;

    private Label _summaryLabel;
    private ListView _resultListView;
    private ColumnHeader _pathHeader;
    private ColumnHeader _statusHeader;
    private ColumnHeader _errorHeader;
    private Button _copyButton;

    public OrphanedProfilesReportPanel()
    {
        InitializeComponent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _summaryLabel = new Label();
        _resultListView = new ListView();
        _pathHeader = new ColumnHeader();
        _statusHeader = new ColumnHeader();
        _errorHeader = new ColumnHeader();
        _copyButton = new Button();

        SuspendLayout();
        Size = new Size(900, 640);

        // _summaryLabel
        _summaryLabel.Location = new Point(15, 10);
        _summaryLabel.AutoSize = true;
        _summaryLabel.Font = new Font(DefaultFont.FontFamily, 9.5f, FontStyle.Bold);

        // _resultListView
        _resultListView.Location = new Point(15, 38);
        _resultListView.Size = new Size(870, 545);
        _resultListView.View = View.Details;
        _resultListView.FullRowSelect = true;
        _resultListView.GridLines = true;
        _resultListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _resultListView.ShowGroups = true;
        _resultListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // _pathHeader
        _pathHeader.Text = "Path";
        _pathHeader.Width = 540;

        // _statusHeader
        _statusHeader.Text = "Status";
        _statusHeader.Width = 100;

        // _errorHeader
        _errorHeader.Text = "Error";
        _errorHeader.Width = 200;

        _resultListView.Columns.AddRange(new ColumnHeader[] { _pathHeader, _statusHeader, _errorHeader });

        // _copyButton
        _copyButton.Text = "Copy to Clipboard";
        _copyButton.Location = new Point(15, 595);
        _copyButton.Size = new Size(140, 28);
        _copyButton.FlatStyle = FlatStyle.System;
        _copyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _copyButton.Click += OnCopyClick;

        // OrphanedProfilesReportPanel
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Controls.AddRange(new Control[] { _summaryLabel, _resultListView, _copyButton });

        ResumeLayout(false);
        PerformLayout();
    }
}

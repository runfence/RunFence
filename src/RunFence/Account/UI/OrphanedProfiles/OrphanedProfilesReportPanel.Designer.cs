#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.OrphanedProfiles;

partial class OrphanedProfilesReportPanel
{
    private IContainer components = null;

    private Label _summaryLabel;
    private ListView _resultListView;
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
        _copyButton = new Button();

        SuspendLayout();

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
        _resultListView.Columns.Add("Path", 540);
        _resultListView.Columns.Add("Status", 100);
        _resultListView.Columns.Add("Error", 200);

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

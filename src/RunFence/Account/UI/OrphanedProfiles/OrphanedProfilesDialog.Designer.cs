#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.OrphanedProfiles;

partial class OrphanedProfilesDialog
{
    private IContainer components = null;

    private Panel _contentPanel;
    private Button _deleteButton;
    private Button _closeButton;

    private OrphanedProfilesDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _selectionPanel?.Dispose();
            _reportPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _contentPanel = new Panel();
        _deleteButton = new Button();
        _closeButton = new Button();

        SuspendLayout();

        // _contentPanel
        _contentPanel.Location = new Point(0, 0);
        _contentPanel.Size = new Size(900, 635);
        _contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // _deleteButton
        _deleteButton.Text = "Delete Selected";
        _deleteButton.Location = new Point(675, 658);
        _deleteButton.Size = new Size(120, 28);
        _deleteButton.FlatStyle = FlatStyle.System;
        _deleteButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _deleteButton.Click += OnDeleteSelectedClick;

        // _closeButton
        _closeButton.Text = "Close";
        _closeButton.DialogResult = DialogResult.Cancel;
        _closeButton.Location = new Point(805, 658);
        _closeButton.Size = new Size(80, 28);
        _closeButton.FlatStyle = FlatStyle.System;
        _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        // OrphanedProfilesDialog
        Text = "Delete Orphaned Profiles";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 700);
        MinimumSize = new Size(700, 500);
        CancelButton = _closeButton;
        Controls.AddRange(new Control[] { _contentPanel, _deleteButton, _closeButton });

        ResumeLayout(false);
        PerformLayout();
    }
}

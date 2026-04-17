using RunFence.Account.OrphanedProfiles;
using RunFence.Apps.UI;
using RunFence.Infrastructure;

namespace RunFence.Account.UI.OrphanedProfiles;

public partial class OrphanedProfilesDialog : Form
{
    private enum DialogState { Loading, Results, Error }

    private readonly IOrphanedProfileService _service;
    private readonly OperationGuard _operationGuard = new();

    private OrphanedProfilesSelectionPanel? _selectionPanel;
    private OrphanedProfilesReportPanel? _reportPanel;
    private DialogState _dialogState;

    public OrphanedProfilesDialog(IOrphanedProfileService orphanedProfileService)
    {
        _service = orphanedProfileService;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        LoadProfilesAsync();
    }

    private void ClearContentPanel()
    {
        // Dispose transient controls (loading/error labels); panels are reused and must not be disposed.
        var toDispose = _contentPanel.Controls.OfType<Label>().ToList();
        _contentPanel.Controls.Clear();
        foreach (var c in toDispose)
            c.Dispose();
    }

    private void ShowPanel(UserControl panel)
    {
        ClearContentPanel();
        panel.Dock = DockStyle.Fill;
        _contentPanel.Controls.Add(panel);
    }

    private void OnDeleteButtonClick(object? sender, EventArgs e)
    {
        switch (_dialogState)
        {
            case DialogState.Results:
                DeleteSelectedAsync();
                break;
            case DialogState.Error:
                LoadProfilesAsync();
                break;
        }
    }

    private async void LoadProfilesAsync()
    {
        _dialogState = DialogState.Loading;
        _deleteButton.Text = "Delete Selected";
        _deleteButton.Enabled = false;
        _deleteButton.Visible = true;
        ClearContentPanel();

        var loadingLabel = new Label
        {
            Location = new Point(15, 15),
            Size = new Size(870, 35),
            AutoSize = false,
            Text = "Scanning for orphaned profiles..."
        };
        _contentPanel.Controls.Add(loadingLabel);
        UseWaitCursor = true;

        List<OrphanedProfile> profiles;
        try
        {
            profiles = await Task.Run(() => _service.GetOrphanedProfiles());
        }
        catch (Exception)
        {
            if (IsDisposed)
                return;
            UseWaitCursor = false;
            _dialogState = DialogState.Error;
            ClearContentPanel();
            _contentPanel.Controls.Add(new Label
            {
                Location = new Point(15, 15),
                Size = new Size(870, 35),
                AutoSize = false,
                Text = "Failed to scan for orphaned profiles."
            });
            _deleteButton.Text = "Rescan";
            _deleteButton.Enabled = true;
            return;
        }

        if (IsDisposed)
            return;
        UseWaitCursor = false;
        ShowSelectionPhase(profiles);
    }

    private void ShowSelectionPhase(List<OrphanedProfile> profiles)
    {
        _dialogState = DialogState.Results;
        _selectionPanel ??= new OrphanedProfilesSelectionPanel();
        _selectionPanel.Populate(profiles);
        ShowPanel(_selectionPanel);
        _deleteButton.Enabled = profiles.Count > 0;
    }

    private async void DeleteSelectedAsync()
    {
        if (_selectionPanel == null)
            return;

        var checkedProfiles = _selectionPanel.CheckedProfiles.ToList();
        if (checkedProfiles.Count == 0)
        {
            MessageBox.Show("No profiles selected.", "Delete Profiles",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var noun = checkedProfiles.Count == 1 ? "directory" : "directories";
        var confirm = MessageBox.Show(
            $"Delete {checkedProfiles.Count} selected profile {noun}? This cannot be undone.",
            "Confirm Deletion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        _operationGuard.Begin(this);
        UseWaitCursor = true;
        try
        {
            var result = await Task.Run(() => _service.DeleteProfiles(checkedProfiles));
            ShowReportPhase(result.Deleted, result.Failed);
        }
        finally
        {
            UseWaitCursor = false;
            _operationGuard.End(this);
        }
    }

    private void ShowReportPhase(List<string> deleted, List<(string Path, string Error)> failed)
    {
        if (failed.Count > 0)
        {
            _dialogState = DialogState.Error;
            _deleteButton.Text = "Rescan";
            _deleteButton.Enabled = true;
        }
        else
        {
            _deleteButton.Visible = false;
            _closeButton.Location = new Point(805, 658);
            _closeButton.Size = new Size(85, 28);
        }

        _reportPanel ??= new OrphanedProfilesReportPanel();
        _reportPanel.Populate(deleted, failed);
        ShowPanel(_reportPanel);
    }
}
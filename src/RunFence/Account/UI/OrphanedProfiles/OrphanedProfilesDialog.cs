using RunFence.Account.OrphanedProfiles;
using RunFence.Apps.UI;
using RunFence.Infrastructure;

namespace RunFence.Account.UI.OrphanedProfiles;

public partial class OrphanedProfilesDialog : RunFence.UI.Forms.ContextHelpForm
{
    private enum DialogState { Loading, Results, Error }
    private enum InitialLoadMode { Scan, ShowProfiles, ShowError }

    private readonly IOrphanedProfileService _service;
    private readonly OperationGuard _operationGuard = new();
    private readonly List<OrphanedProfile>? _initialProfiles;
    private readonly InitialLoadMode _initialLoadMode;

    private OrphanedProfilesSelectionPanel? _selectionPanel;
    private OrphanedProfilesReportPanel? _reportPanel;
    private DialogState _dialogState;
    private bool _initialized;

    public OrphanedProfilesDialog(IOrphanedProfileService orphanedProfileService)
        : this(orphanedProfileService, null, InitialLoadMode.Scan)
    {
    }

    public OrphanedProfilesDialog(IOrphanedProfileService orphanedProfileService, List<OrphanedProfile> initialProfiles)
        : this(orphanedProfileService, initialProfiles, InitialLoadMode.ShowProfiles)
    {
    }

    public static OrphanedProfilesDialog CreateScanErrorDialog(IOrphanedProfileService orphanedProfileService)
        => new(orphanedProfileService, null, InitialLoadMode.ShowError);

    private OrphanedProfilesDialog(
        IOrphanedProfileService orphanedProfileService,
        List<OrphanedProfile>? initialProfiles,
        InitialLoadMode initialLoadMode)
    {
        ArgumentNullException.ThrowIfNull(orphanedProfileService);
        _service = orphanedProfileService;
        _initialProfiles = initialProfiles?.ToList();
        _initialLoadMode = initialLoadMode;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_initialized)
            return;

        _initialized = true;
        if (_initialProfiles != null)
        {
            ShowSelectionPhase(_initialProfiles);
            return;
        }

        if (_initialLoadMode == InitialLoadMode.ShowError)
        {
            ShowScanErrorPhase();
            return;
        }

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
        _selectionPanel?.StopSizeCalculation();
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
            ShowScanErrorPhase();
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
        _selectionPanel ??= new OrphanedProfilesSelectionPanel(_service);
        _selectionPanel.Populate(profiles);
        ShowPanel(_selectionPanel);
        _deleteButton.Enabled = profiles.Count > 0;
    }

    private void ShowScanErrorPhase()
    {
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
            $"Move {checkedProfiles.Count} selected profile {noun} to the Recycle Bin?",
            "Move to Recycle Bin",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        _selectionPanel.StopSizeCalculation();
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
        _selectionPanel?.StopSizeCalculation();

        if (failed.Count > 0)
        {
            _dialogState = DialogState.Error;
            _deleteButton.Text = "Rescan";
            _deleteButton.Enabled = true;
        }
        else
        {
            _deleteButton.Visible = false;
        }

        _reportPanel ??= new OrphanedProfilesReportPanel();
        _reportPanel.Populate(deleted, failed);
        ShowPanel(_reportPanel);
    }
}

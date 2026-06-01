using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public sealed class SidMigrationWorkflowController : IDisposable
{
    private readonly SessionContext _session;
    private readonly ISidMigrationService _sidMigrationService;
    private readonly IMessageBoxService _messageBoxService;
    private readonly SidMigrationWorkflowState _state;
    private readonly SidMigrationProgressCoordinator _progressCoordinator;
    private readonly ISidMigrationStepFactory _stepFactory;
    private readonly SidMigrationDiscoveryStepController _discoveryStepController;
    private readonly SidMigrationDiskScanStepController _diskScanStepController;

    private ISidMigrationPathStepView? _pathStep;
    private ISidMigrationMappingStepView? _mappingStep;
    private Func<IWin32Window, Task>? _pendingStepActivation;

    public SidMigrationWorkflowController(
        SessionContext session,
        ISidMigrationService sidMigrationService,
        IMessageBoxService messageBoxService,
        SidMigrationWorkflowState state,
        SidMigrationProgressCoordinator progressCoordinator,
        ISidMigrationStepFactory stepFactory,
        SidMigrationDiscoveryStepController discoveryStepController,
        SidMigrationDiskScanStepController diskScanStepController)
    {
        _session = session;
        _sidMigrationService = sidMigrationService;
        _messageBoxService = messageBoxService;
        _state = state;
        _progressCoordinator = progressCoordinator;
        _stepFactory = stepFactory;
        _discoveryStepController = discoveryStepController;
        _diskScanStepController = diskScanStepController;
    }

    public event Action? ViewChanged;

    public event Action? CloseRequested;

    public SidMigrationDialogViewState CurrentView { get; private set; } = null!;

    public bool InAppMigrationApplied => _state.InAppMigrationApplied;

    public void Initialize()
    {
        ShowStep(1);
    }

    public void HandleViewShown(IWin32Window owner)
    {
        var activation = Interlocked.Exchange(ref _pendingStepActivation, null);
        if (activation == null)
            return;

        _ = activation(owner);
    }

    public void HandleNext(IWin32Window owner)
    {
        switch (_state.CurrentStep)
        {
            case 1:
                if (_pathStep != null)
                {
                    _state.RootPaths = _pathStep.CollectSelectedPaths();
                    _state.SavedPathState = _pathStep.SavedState;
                }

                if (_state.RootPaths.Count == 0)
                {
                    _messageBoxService.Show(owner, "Please select at least one path.", "No Paths", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ShowStep(_state.Mappings.Count > 0 ? 4 : 2);
                break;

            case 2:
                _state.DiscoveryState = SidMigrationDiscoveryState.Performed;
                ShowStep(3);
                break;

            case 3:
                if (_mappingStep == null || !_mappingStep.TryCollectSelections(out var mappings, out var sidsToDelete))
                    return;

                _state.Mappings = mappings;
                _state.SidsToDelete = sidsToDelete;

                if (_state.Mappings.Count + _state.SidsToDelete.Count == 0)
                {
                    _messageBoxService.Show(
                        owner,
                        "Please configure at least one action (Migrate or Delete).",
                        "No Actions",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (_state.RootPaths.Count == 0)
                {
                    ShowStep(1);
                    return;
                }

                ShowStep(4);
                break;

            case 4:
                ShowStep(5);
                break;

            case 5:
                ShowStep(6);
                break;

            case 6:
                ShowStep(7);
                break;

            case 7:
                CloseRequested?.Invoke();
                break;
        }
    }

    public void HandleSecondary(IWin32Window owner)
    {
        if (CanShowBack())
        {
            if (_progressCoordinator.TryHandleSecondaryAction(_state.CurrentStep, canShowBack: true, owner))
                return;

            NavigateBack();
            return;
        }

        if (_progressCoordinator.TryHandleSecondaryAction(_state.CurrentStep, canShowBack: false, owner))
            return;

        CloseRequested?.Invoke();
    }

    public bool HandleEscape(IWin32Window owner)
    {
        if (_progressCoordinator.TryHandleSecondaryAction(_state.CurrentStep, CanShowBack(), owner))
            return true;

        if (!CurrentView.SecondaryActsAsCancel || !CurrentView.SecondaryEnabled)
            return false;

        HandleSecondary(owner);
        return true;
    }

    public void HandleFormClosing(IWin32Window owner, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing)
            return;

        if (_state.CurrentStep == 6)
        {
            e.Cancel = true;
            if (_progressCoordinator.IsInProgress)
                _progressCoordinator.TryHandleSecondaryAction(_state.CurrentStep, canShowBack: false, owner);
            else
                _progressCoordinator.ShowStep6CloseBlockedMessage(owner);
            return;
        }

        if (!_progressCoordinator.IsInProgress)
            return;

        e.Cancel = true;
        _progressCoordinator.TryHandleSecondaryAction(_state.CurrentStep, canShowBack: false, owner);
    }

    public DialogResult GetCloseDialogResult()
    {
        return _state.InAppMigrationApplied ? DialogResult.OK : DialogResult.Cancel;
    }

    public void Dispose()
    {
        _progressCoordinator.Dispose();
    }

    private void ShowStep(int step)
    {
        _state.SetCurrentStep(step);
        _pathStep = null;
        _mappingStep = null;
        _pendingStepActivation = null;

        switch (step)
        {
            case 1:
                ShowStep1PathSelection();
                break;
            case 2:
                ShowStep2DiscoveryScan();
                break;
            case 3:
                ShowStep3MappingReview();
                break;
            case 4:
                ShowStep4DiskScan();
                break;
            case 5:
                ShowStep5DiskPreview();
                break;
            case 6:
                ShowStep6DiskApply();
                break;
            case 7:
                ShowStep7InAppMigration();
                break;
        }
    }

    private void ShowStep1PathSelection()
    {
        var returningFromManual = _state.Mappings.Count > 0;
        var pathStep = _stepFactory.CreatePathStep(showSkipButton: !returningFromManual);
        if (_state.SavedPathState != null)
            pathStep.RestoreState(_state.SavedPathState);

        pathStep.SkipRequested += (_, _) =>
        {
            _state.RootPaths = pathStep.CollectSelectedPaths();
            _state.SavedPathState = pathStep.SavedState;
            _state.DiscoveryState = SidMigrationDiscoveryState.Skipped;
            ShowStep(3);
        };

        _pathStep = pathStep;

        UpdateView(
            pathStep,
            returningFromManual ? "Step 1: Select Paths to Re-scan" : "Step 1: Select Paths to Scan",
            returningFromManual ? "Start Scan" : "Discover",
            nextEnabled: true,
            nextVisible: true,
            secondaryEnabled: true);
    }

    private void ShowStep2DiscoveryScan()
    {
        var progressStep = _stepFactory.CreateDiscoveryProgressStep();
        _pendingStepActivation = async owner =>
        {
            var (_, statusLabel, ct) = _progressCoordinator.BeginProgressStep(progressStep);
            var progress = new Progress<(long scanned, long sidsFound)>(p =>
            {
                if (!progressStep.View.IsDisposed)
                    statusLabel.Text = $"Scanned: {p.scanned:N0} items, Found: {p.sidsFound:N0} unique SIDs";
            });

            await _progressCoordinator.RunGuardedAsync(async () =>
            {
                _state.OrphanedSids = await _sidMigrationService.DiscoverOrphanedSidsAsync(_state.RootPaths, progress, ct);
            }, "Discovery scan", progressStep, onCompleted: () =>
            {
                _state.DiscoveryState = SidMigrationDiscoveryState.Performed;
                var result = _discoveryStepController.BuildResult(_state.OrphanedSids);
                statusLabel.Text = result.CompletionText;

                if (result.UnresolvedWarningMessage != null)
                {
                    _messageBoxService.Show(
                        owner,
                        result.UnresolvedWarningMessage,
                        "Discovery Finished",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                ShowStep(3);
            }, onCancel: () =>
            {
                statusLabel.Text = _discoveryStepController.BuildResult(_state.OrphanedSids).CancelText;
                UpdateView(nextEnabled: _state.OrphanedSids.Count > 0, secondaryEnabled: true);
            }, onNavigateBackAfterCancel: NavigateBack);
        };

        UpdateView(
            progressStep,
            "Step 2: Discovering Orphaned SIDs",
            "Next",
            nextEnabled: false,
            nextVisible: true,
            secondaryEnabled: true);
    }

    private void ShowStep3MappingReview()
    {
        var mappingStep = _stepFactory.CreateMappingStep(_state.OrphanedSids);
        _mappingStep = mappingStep;
        _pendingStepActivation = _ =>
        {
            mappingStep.BeginAsync(
                onReady: () => UpdateView(nextEnabled: true, secondaryEnabled: true),
                onFailed: () => UpdateView(nextEnabled: false, secondaryEnabled: true));
            return Task.CompletedTask;
        };

        UpdateView(
            mappingStep,
            "Step 3: Review SID Mappings",
            _state.DiscoveryState == SidMigrationDiscoveryState.Performed ? "Execute" : "Start Scan",
            nextEnabled: false,
            nextVisible: true,
            secondaryEnabled: false);
    }

    private void ShowStep4DiskScan()
    {
        var progressStep = _stepFactory.CreateDiskScanProgressStep();
        _pendingStepActivation = async owner =>
        {
            var (_, statusLabel, ct) = _progressCoordinator.BeginProgressStep(progressStep);
            var progress = new Progress<(long scanned, long found)>(p =>
            {
                if (!progressStep.View.IsDisposed)
                    statusLabel.Text = $"Scanned: {p.scanned:N0}, Found: {p.found:N0} matches";
            });

            await _progressCoordinator.RunGuardedAsync(async () =>
            {
                _state.ScanResults = await _sidMigrationService.ScanAsync(
                    _state.RootPaths,
                    _state.Mappings,
                    _state.SidsToDelete,
                    progress,
                    ct);
            }, "Disk scan", progressStep, onCompleted: () =>
            {
                ShowStep(5);
            }, onCancel: () =>
            {
                statusLabel.Text = _diskScanStepController.BuildResult(_state.ScanResults).CancelText;
                UpdateView(nextEnabled: _state.ScanResults.Count > 0, secondaryEnabled: true);
            }, onNavigateBackAfterCancel: NavigateBack);
        };

        UpdateView(
            progressStep,
            "Step 4: Scanning Disk",
            "Next",
            nextEnabled: false,
            nextVisible: true,
            secondaryEnabled: true);
    }

    private void ShowStep5DiskPreview()
    {
        UpdateView(
            _stepFactory.CreatePreviewStep(_state.ScanResults),
            "Step 5: Preview Changes",
            "Apply",
            nextEnabled: true,
            nextVisible: true,
            secondaryEnabled: true);
    }

    private void ShowStep6DiskApply()
    {
        var progressStep = _stepFactory.CreateDiskApplyProgressStep();
        _pendingStepActivation = async _ =>
        {
            var (progressBar, statusLabel, ct) = _progressCoordinator.BeginProgressStep(
                progressStep,
                "Applying...",
                Math.Max(_state.ScanResults.Count, 1),
                showCancelButton: false);

            var progress = new Progress<MigrationProgress>(p =>
            {
                if (!progressStep.View.IsDisposed)
                {
                    progressBar.Value = Math.Min((int)p.Applied, progressBar.Maximum);
                    statusLabel.Text = $"Applied: {p.Applied:N0} / {p.Total:N0}";
                    progressStep.SetCurrentPath(p.CurrentPath);
                }
            });

            await _progressCoordinator.RunGuardedAsync(async () =>
            {
                _state.ApplyResult = await _sidMigrationService.ApplyAsync(
                    _state.ScanResults,
                    _state.Mappings,
                    _state.SidsToDelete,
                    progress,
                    ct);
            }, "Disk apply", progressStep, onCompleted: () =>
            {
                progressBar.Value = progressBar.Maximum;
                statusLabel.Text = $"Done. Applied: {_state.ApplyResult.applied:N0}, Errors: {_state.ApplyResult.errors:N0}";
                UpdateView(nextEnabled: true, secondaryEnabled: true);
            }, onCancel: () =>
            {
                statusLabel.Text = "Apply cancelled.";
            }, onNavigateBackAfterCancel: NavigateBack, resetProgressOnCancel: false);
        };

        UpdateView(
            progressStep,
            "Step 6: Applying Changes",
            "Next",
            nextEnabled: false,
            nextVisible: true,
            secondaryEnabled: true);
    }

    private void ShowStep7InAppMigration()
    {
        var referencedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var db = _session.Database;
        var store = _session.CredentialStore;

        foreach (var app in db.Apps)
        {
            referencedSids.Add(app.AccountSid);
            app.AllowedIpcCallers?.ForEach(sid => referencedSids.Add(sid));
            app.AllowedAclEntries?.ForEach(a => referencedSids.Add(a.Sid));
        }

        foreach (var account in db.Accounts)
            referencedSids.Add(account.Sid);
        store.Credentials.ForEach(c => referencedSids.Add(c.Sid));

        referencedSids.Remove("");
        var filteredMappings = _state.Mappings.Where(m => referencedSids.Contains(m.OldSid)).ToList();
        var filteredDeletes = _state.SidsToDelete.Where(s => referencedSids.Contains(s)).ToList();
        var unresolvedSids = _state.OrphanedSids
            .Where(s => s.Classification == OrphanedSidClassification.Unresolved)
            .Select(s => s.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inAppStep = _stepFactory.CreateInAppStep(filteredMappings, filteredDeletes, unresolvedSids);
        inAppStep.MigrationApplied += (_, _) => _state.InAppMigrationApplied = true;

        UpdateView(
            inAppStep,
            "Step 7: In-App Data Migration",
            "Close",
            nextEnabled: true,
            nextVisible: true,
            secondaryEnabled: true);
    }

    private bool CanShowBack()
    {
        if (_state.CurrentStep == 3)
            return _state.DiscoveryState == SidMigrationDiscoveryState.Skipped;

        return _state.CurrentStep > 1 && _state.CurrentStep != 6 && _state.CurrentStep != 7;
    }

    private void NavigateBack()
    {
        switch (_state.CurrentStep)
        {
            case 3:
                ShowStep(1);
                break;
            case 5:
                ShowStep(3);
                break;
            default:
                if (_state.CurrentStep > 1)
                    ShowStep(_state.CurrentStep - 1);
                break;
        }
    }

    private void UpdateView(
        ISidMigrationStepView? stepView = null,
        string? title = null,
        string? nextText = null,
        bool? nextEnabled = null,
        bool? nextVisible = null,
        bool? secondaryEnabled = null)
    {
        var resolvedStepView = stepView ?? CurrentView.StepView;
        var resolvedTitle = title ?? CurrentView.Title;
        var resolvedNextText = nextText ?? CurrentView.NextText;
        var resolvedNextEnabled = nextEnabled ?? CurrentView.NextEnabled;
        var resolvedNextVisible = nextVisible ?? CurrentView.NextVisible;
        var resolvedSecondaryEnabled = secondaryEnabled ?? CurrentView.SecondaryEnabled;
        var secondaryActsAsCancel = !CanShowBack();

        CurrentView = new SidMigrationDialogViewState(
            resolvedStepView,
            resolvedTitle,
            resolvedNextText,
            resolvedNextEnabled,
            resolvedNextVisible,
            secondaryActsAsCancel ? "Cancel" : "Back",
            resolvedSecondaryEnabled,
            secondaryActsAsCancel);

        ViewChanged?.Invoke();
    }
}

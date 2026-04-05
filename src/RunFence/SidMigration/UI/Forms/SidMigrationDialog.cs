using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI.Forms;

public partial class SidMigrationDialog : Form
{
    private readonly SessionContext _session;
    private readonly ISidMigrationService _sidMigrationService;
    private readonly InAppMigrationHandler _inAppMigrationHandler;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ILoggingService _log;
    private readonly ISidResolver _sidResolver;
    private readonly ISidNameCacheService _sidNameCache;

    private int _currentStep;
    private MigrationMappingStep? _mappingStep;
    private SidMigrationPathStep? _pathStep;
    private List<(string path, bool isChecked)>? _savedPathState;
    private CancellationTokenSource? _cts;
    private readonly OperationGuard _operationGuard = new();

    // Data shared between steps
    private List<string> _rootPaths = new();
    private List<SidMigrationMapping> _mappings = new();
    private List<string> _sidsToDelete = new();
    private List<OrphanedSid> _orphanedSids = new();
    private List<SidMigrationMatch> _scanResults = new();
    private (long applied, long errors) _applyResult;
    private bool _skippedDiscovery;

    public bool InAppMigrationApplied { get; private set; }

    public SidMigrationDialog(
        SessionContext session,
        ISidMigrationService sidMigrationService,
        InAppMigrationHandler inAppMigrationHandler,
        ILocalUserProvider localUserProvider,
        ILoggingService log,
        ISidResolver sidResolver,
        ISidNameCacheService sidNameCache)
    {
        _session = session;
        _sidMigrationService = sidMigrationService;
        _inAppMigrationHandler = inAppMigrationHandler;
        _localUserProvider = localUserProvider;
        _log = log;
        _sidResolver = sidResolver;
        _sidNameCache = sidNameCache;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        ShowStep(1);
    }

    private void ShowStep(int step)
    {
        _currentStep = step;
        _stepPanel.Controls.Clear();
        _backButton.Enabled = step > 1 && step != 2 && step != 4 && step != 6;
        _nextButton.Enabled = true;

        switch (step)
        {
            case 1:
                ShowStep1_PathSelection();
                break;
            case 2:
                ShowStep2_DiscoveryScan();
                break;
            case 3:
                ShowStep3_MappingReview();
                break;
            case 4:
                ShowStep4_DiskScan();
                break;
            case 5:
                ShowStep5_DiskPreview();
                break;
            case 6:
                ShowStep6_DiskApply();
                break;
            case 7:
                ShowStep7_InAppMigration();
                break;
        }
    }

    // --- Shared progress step helpers ---

    private (ProgressBar progressBar, Label statusLabel, CancellationToken ct) BeginProgressStep(
        string statusText = "Scanning...", int? maxValue = null, bool showCancelButton = true)
    {
        _nextButton.Enabled = false;
        _backButton.Enabled = false;
        _nextButton.Text = "Next";

        var step = new MigrationProgressStep();
        step.Dock = DockStyle.Top;
        step.Configure(statusText, maxValue, showCancelButton);
        _stepPanel.Controls.Add(step);
        _stepPanel.Controls.SetChildIndex(step, 0);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        step.CancelButton.Click += (_, _) => _cts?.Cancel();

        _operationGuard.Begin();
        return (step.ProgressBar, step.StatusLabel, _cts.Token);
    }

    private async Task RunGuardedAsync(Func<Task> operation, string errorLogPrefix, ProgressBar progressBar, Label statusLabel,
        Action? onCancel = null, bool resetProgressOnCancel = true)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                if (resetProgressOnCancel)
                {
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Value = 0;
                }

                _backButton.Enabled = _currentStep > 1 && _currentStep != 2 && _currentStep != 4 && _currentStep != 6;
                onCancel?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{errorLogPrefix} failed", ex);
            if (!IsDisposed)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                statusLabel.Text = $"Error: {ex.Message}";
                _backButton.Enabled = _currentStep > 1 && _currentStep != 2 && _currentStep != 4 && _currentStep != 6;
            }
        }
        finally
        {
            _operationGuard.End();
        }
    }

    // --- Step 1: Path Selection ---

    private void ShowStep1_PathSelection()
    {
        var returningFromManual = _mappings.Count > 0;
        _stepTitleLabel.Text = returningFromManual || _skippedDiscovery
            ? "Select Paths to Scan"
            : "Step 1: Select Paths to Scan";
        _nextButton.Text = returningFromManual ? "Start Scan" : "Discover";

        _pathStep = new SidMigrationPathStep(showSkipButton: !returningFromManual)
        {
            Dock = DockStyle.Fill
        };

        if (_savedPathState != null)
            _pathStep.RestoreState(_savedPathState);

        _pathStep.SkipRequested += (_, _) =>
        {
            _rootPaths = _pathStep.CollectSelectedPaths();
            _savedPathState = _pathStep.SavedState;
            _skippedDiscovery = true;
            ShowStep(3);
        };

        _stepPanel.Controls.Add(_pathStep);
    }

    private void CollectRootPaths()
    {
        if (_pathStep == null)
            return;
        _rootPaths = _pathStep.CollectSelectedPaths();
        _savedPathState = _pathStep.SavedState;
    }

    // --- Step 2: Discovery Scan ---

    private void ShowStep2_DiscoveryScan()
    {
        _stepTitleLabel.Text = "Step 2: Discovering Orphaned SIDs";
        _skippedDiscovery = false;

        var (progressBar, statusLabel, ct) = BeginProgressStep();
        var progress = new Progress<(long scanned, long sidsFound)>(p =>
        {
            if (!IsDisposed)
                statusLabel.Text = $"Scanned: {p.scanned:N0} items, Found: {p.sidsFound:N0} unique SIDs";
        });

        _ = RunGuardedAsync(async () =>
        {
            _orphanedSids = await _sidMigrationService.DiscoverOrphanedSidsAsync(
                _rootPaths, progress, ct);

            if (!IsDisposed)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                statusLabel.Text = $"Done. Found {_orphanedSids.Count} orphaned SIDs.";
                _nextButton.Enabled = true;
            }
        }, "Discovery scan", progressBar, statusLabel, onCancel: () =>
        {
            statusLabel.Text = _orphanedSids.Count > 0
                ? $"Scan cancelled. Found {_orphanedSids.Count} orphaned SIDs so far."
                : "Scan cancelled.";
            _nextButton.Enabled = _orphanedSids.Count > 0;
        });
    }

    // --- Step 3: Mapping Review ---

    private void ShowStep3_MappingReview()
    {
        _stepTitleLabel.Text = "Step 3: Review SID Mappings";
        _nextButton.Text = "Start Scan";
        _nextButton.Enabled = false;
        _backButton.Enabled = false;

        _mappingStep = new MigrationMappingStep(
            _session, _sidMigrationService, _localUserProvider, _log, _orphanedSids, _sidResolver, _sidNameCache)
        {
            Dock = DockStyle.Fill
        };
        _stepPanel.Controls.Add(_mappingStep);

        _mappingStep.BeginAsync(
            onReady: () =>
            {
                _nextButton.Enabled = true;
                _backButton.Enabled = true;
            },
            onFailed: () => { _backButton.Enabled = true; });
    }

    // --- Step 4: Disk Scan ---

    private void ShowStep4_DiskScan()
    {
        _stepTitleLabel.Text = "Step 4: Scanning Disk";

        var (progressBar, statusLabel, ct) = BeginProgressStep();
        var progress = new Progress<(long scanned, long found)>(p =>
        {
            if (!IsDisposed)
                statusLabel.Text = $"Scanned: {p.scanned:N0}, Found: {p.found:N0} matches";
        });

        _ = RunGuardedAsync(async () =>
        {
            _scanResults = await _sidMigrationService.ScanAsync(_rootPaths, _mappings, progress, ct);

            if (!IsDisposed)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                statusLabel.Text = $"Done. Found {_scanResults.Count:N0} items to update.";
                _nextButton.Enabled = true;
            }
        }, "Disk scan", progressBar, statusLabel, onCancel: () =>
        {
            statusLabel.Text = _scanResults.Count > 0
                ? $"Scan cancelled. Found {_scanResults.Count:N0} items so far."
                : "Scan cancelled.";
            _nextButton.Enabled = _scanResults.Count > 0;
        });
    }

    // --- Step 5: Disk Preview ---

    private void ShowStep5_DiskPreview()
    {
        _stepTitleLabel.Text = "Step 5: Preview Changes";
        _nextButton.Text = "Apply";
        _backButton.Enabled = true;

        var step = new SidMigrationPreviewStep(_scanResults) { Dock = DockStyle.Fill };
        _stepPanel.Controls.Add(step);
    }

    // --- Step 6: Disk Apply ---

    private void ShowStep6_DiskApply()
    {
        _stepTitleLabel.Text = "Step 6: Applying Changes";

        var (progressBar, statusLabel, ct) = BeginProgressStep("Applying...", Math.Max(_scanResults.Count, 1),
            showCancelButton: false);

        var pathLabel = new Label
        {
            Location = new Point(15, 95),
            Size = new Size(560, 25),
            ForeColor = Color.DarkGray,
            Font = new Font(Font.FontFamily, 8f)
        };
        _stepPanel.Controls.Add(pathLabel);

        var progress = new Progress<MigrationProgress>(p =>
        {
            if (!IsDisposed)
            {
                progressBar.Value = Math.Min((int)p.Applied, progressBar.Maximum);
                statusLabel.Text = $"Applied: {p.Applied:N0} / {p.Total:N0}";
                pathLabel.Text = p.CurrentPath;
            }
        });

        _ = RunGuardedAsync(async () =>
        {
            _applyResult = await _sidMigrationService.ApplyAsync(_scanResults, _mappings, progress, ct);

            if (!IsDisposed)
            {
                progressBar.Value = progressBar.Maximum;
                statusLabel.Text = $"Done. Applied: {_applyResult.applied:N0}, Errors: {_applyResult.errors:N0}";
                _nextButton.Enabled = true;
            }
        }, "Disk apply", progressBar, statusLabel, onCancel: () => { statusLabel.Text = "Apply cancelled."; }, resetProgressOnCancel: false);
    }

    // --- Step 7: In-App Migration ---

    private void ShowStep7_InAppMigration()
    {
        _stepTitleLabel.Text = "Step 7: In-App Data Migration";
        _nextButton.Visible = false;
        _backButton.Enabled = false;
        _cancelCloseButton.Text = "Close";

        var referencedSids = CollectReferencedSids();
        var filteredMappings = _mappings.Where(m => referencedSids.Contains(m.OldSid)).ToList();
        var filteredDeletes = _sidsToDelete.Where(s => referencedSids.Contains(s)).ToList();

        var step = new SidMigrationInAppStep(_inAppMigrationHandler, _session, filteredMappings, filteredDeletes, _sidResolver)
        {
            Dock = DockStyle.Fill
        };
        step.MigrationApplied += (_, _) => InAppMigrationApplied = true;
        _stepPanel.Controls.Add(step);
    }

    private HashSet<string> CollectReferencedSids()
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var db = _session.Database;
        var store = _session.CredentialStore;

        foreach (var app in db.Apps)
        {
            sids.Add(app.AccountSid);
            app.AllowedIpcCallers?.ForEach(sid => sids.Add(sid));
            app.AllowedAclEntries?.ForEach(a => sids.Add(a.Sid));
        }

        foreach (var account in db.Accounts)
            sids.Add(account.Sid);
        store.Credentials.ForEach(c => sids.Add(c.Sid));

        sids.Remove("");
        return sids;
    }

    // --- Navigation ---

    private void OnBackClick(object? sender, EventArgs e)
    {
        switch (_currentStep)
        {
            case 3:
                ShowStep(1);
                break;
            case 5:
                ShowStep(3);
                break;
            default:
                if (_currentStep > 1)
                    ShowStep(_currentStep - 1);
                break;
        }
    }

    private void OnNextClick(object? sender, EventArgs e)
    {
        switch (_currentStep)
        {
            case 1:
                CollectRootPaths();
                if (_rootPaths.Count == 0)
                {
                    MessageBox.Show("Please select at least one path.", "No Paths", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // If we already have manual mappings, skip discovery and go straight to disk scan
                ShowStep(_mappings.Count > 0 ? 4 : 2);
                break;

            case 2:
                ShowStep(3);
                break;

            case 3:
                _mappings = _mappingStep?.CollectMappings() ?? new List<SidMigrationMapping>();
                _sidsToDelete = _mappingStep?.CollectDeleteSids() ?? new List<string>();
                if (_mappings.Count == 0 && _sidsToDelete.Count == 0)
                {
                    MessageBox.Show("Please configure at least one action (Migrate or Delete).",
                        "No Actions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_mappings.Count > 0 && _rootPaths.Count == 0)
                {
                    // No paths selected yet — redirect to path selection first
                    ShowStep(1);
                    return;
                }

                ShowStep(_mappings.Count > 0 ? 4 : 7);
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
        }
    }

    private void OnCancelCloseClick(object? sender, EventArgs e)
    {
        if (_operationGuard.IsInProgress)
        {
            _cts?.Cancel();
            return;
        }

        DialogResult = InAppMigrationApplied ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _currentStep == 6)
        {
            // Disk apply is irreversible — block close entirely during step 6
            // (whether the operation is still running or has completed).
            // The user must proceed to step 7 (in-app migration) before the dialog
            // can be closed, to ensure in-app data is kept consistent with disk changes.
            e.Cancel = true;
            if (_operationGuard.IsInProgress)
                _cts?.Cancel();
            return;
        }

        if (_operationGuard.IsInProgress && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _cts?.Cancel();
            return;
        }

        _cts?.Dispose();
        base.OnFormClosing(e);
    }
}
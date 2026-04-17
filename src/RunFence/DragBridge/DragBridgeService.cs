using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;

namespace RunFence.DragBridge;

/// <summary>
/// Orchestrates the cross-user drag-and-drop bridge.
/// Single hotkey opens the DragBridgeWindow: drop files onto it to capture them,
/// drag from it to deliver captured files to the target app.
/// </summary>
public class DragBridgeService(
    IGlobalHotkeyService hotkeyService,
    IWindowOwnerDetector windowOwnerDetector,
    INotificationService notifications,
    ILoggingService log,
    IDragBridgeTempFileManager tempManager,
    IDragBridgeSessionManager processLauncher,
    DragBridgeCopyFlow copyFlow,
    DragBridgeResolveOrchestrator resolveOrchestrator)
    : IDragBridgeService, IRequiresInitialization
{
    public const int CopyHotkeyId = 0xDB01;

    private volatile AppDatabase? _database;

    private bool _enabled;
    private int _copyHotkey;
    private long? _lastHotkeyTicks; // null = no hotkey fired yet (bypasses rate limit on first press)
    private bool _rateLimitNotified;

    public void Initialize()
    {
        hotkeyService.HotkeyPressed += OnHotkeyPressed;

        // Best-effort startup cleanup of orphaned temp folders from previous sessions
        log.Info("DragBridgeService: cleaning up orphaned temp folders in background.");
        _ = Task.Run(() =>
        {
            tempManager.CleanupOldFolders(TimeSpan.FromHours(24));
            log.Info("DragBridgeService: orphaned temp folder cleanup complete.");
        });
    }

    public void SetData(SessionContext session)
    {
        _database = session.Database;
        processLauncher.SetData(session);
    }

    public void ApplySettings(AppSettings settings)
    {
        _copyHotkey = settings.DragBridgeCopyHotkey;

        hotkeyService.UnregisterAll();
        _enabled = settings.EnableDragBridge;
        _lastHotkeyTicks = null; // reset rate limit on settings change so new hotkey fires immediately

        if (_enabled)
            RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        // Register always returns true for keyboard-hook-based hotkeys (no OS registration conflicts).
        hotkeyService.Register(CopyHotkeyId,
            DragBridgeHotkeyHelper.SplitModifiers(_copyHotkey),
            DragBridgeHotkeyHelper.GetVirtualKey(_copyHotkey));
    }

    private void OnHotkeyPressed(int id)
    {
        if (id != CopyHotkeyId)
            return;

        // Capture the foreground window immediately — before any focus changes caused by
        // DragBridge launch or ForceToForeground. This is the window to restore on close.
        var restoreHwnd = InfraWindowNative.GetForegroundWindow();

        var nowTicks = Environment.TickCount64;
        if (_lastHotkeyTicks.HasValue && nowTicks - _lastHotkeyTicks.Value < 1000)
        {
            if (!_rateLimitNotified)
            {
                _rateLimitNotified = true; // intentionally never reset — one notification per session, subsequent rate-limit hits are silently logged
                notifications.ShowWarning("Drag Bridge", "Hotkey triggered too quickly — please wait a moment and try again.");
            }
            else
            {
                log.Info("DragBridgeService: hotkey suppressed by rate limit.");
            }

            return;
        }

        _lastHotkeyTicks = nowTicks;

        StartBridgeAsync(restoreHwnd);
    }

    private void StartBridgeAsync(nint restoreHwnd)
    {
        var (captured, sourceSid, _) = copyFlow.GetCapturedFiles();
        bool hasCapture = captured is { Count: > 0 };

        var ownerInfo = hasCapture
            ? windowOwnerDetector.GetForegroundWindowOwnerInfo()
            : windowOwnerDetector.GetDragSourceOrForegroundOwnerInfo();
        if (ownerInfo == null)
        {
            notifications.ShowWarning("Drag Bridge", "Cannot identify window owner.");
            return;
        }

        Cursor.Current = Cursors.WaitCursor;
        processLauncher.KillActiveOperation();
        var cts = processLauncher.BeginOperation();
        var capturedPaths = captured?.ToList() ?? [];

        _ = Task.Run(() => RunBridgeFlowAsync(ownerInfo.Value, capturedPaths, sourceSid, restoreHwnd, cts.Token), cts.Token)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    log.Error("DragBridge flow failed", t.Exception);
                RestoreCursor();
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task RunBridgeFlowAsync(WindowOwnerInfo ownerInfo, List<string> capturedFiles,
        string? sourceSid, nint restoreHwnd, CancellationToken ct)
    {
        var db = _database;
        if (db == null)
            return;

        // Pre-check: if all captured files are already accessible by the target, mark them pre-resolved
        // so the window starts ready to drag without requiring a first-drag resolve-request cycle.
        Func<CancellationToken, Task<List<string>?>>? resolveDelegate = null;
        bool filesPreResolved = false;
        if (capturedFiles.Count > 0 && sourceSid != null)
        {
            try { filesPreResolved = !resolveOrchestrator.NeedsAccessResolution(ownerInfo.Sid, capturedFiles); }
            catch { } // pre-check failure → fall back to lazy resolve

            if (!filesPreResolved)
                resolveDelegate = resolveOrchestrator.CreateResolveDelegate(ownerInfo, capturedFiles, sourceSid, db);
        }

        var cursorPos = Cursor.Position;
        await copyFlow.RunBridgeAsync(ownerInfo, capturedFiles, cursorPos, resolveDelegate, filesPreResolved, restoreHwnd, ct);
    }

    private static void RestoreCursor() => Cursor.Current = Cursors.Default;

    public void Dispose()
    {
        hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        hotkeyService.UnregisterAll();
        processLauncher.KillActiveOperation();
        _ = Task.Run(() => tempManager.CleanupOldFolders(TimeSpan.FromHours(24)));
    }
}
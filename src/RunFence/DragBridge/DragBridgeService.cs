using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

/// <summary>
/// Orchestrates the cross-user drag-and-drop bridge.
/// Single hotkey opens the DragBridgeWindow: drop files onto it to capture them,
/// drag from it to deliver captured files to the target app.
/// </summary>
public class DragBridgeService : IDragBridgeService, IRequiresInitialization
{
    public const int CopyHotkeyId = 0xDB01;

    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IWindowOwnerDetector _windowOwnerDetector;
    private readonly INotificationService _notifications;
    private readonly ILoggingService _log;
    private readonly IDragBridgeTempFileManager _tempManager;
    private readonly DragBridgePasteHandler _pasteHandler;
    private readonly DragBridgeCopyFlow _copyFlow;
    private readonly DragBridgeProcessLauncher _processLauncher;
    private readonly ISessionSaver _sessionSaver;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly IQuickAccessPinService _quickAccessPinService;

    private volatile AppDatabase? _database;

    private bool _enabled;
    private int _copyHotkey;
    private long? _lastHotkeyTicks; // null = no hotkey fired yet (bypasses rate limit on first press)
    private bool _rateLimitNotified;

    public DragBridgeService(
        IGlobalHotkeyService hotkeyService,
        IWindowOwnerDetector windowOwnerDetector,
        INotificationService notifications,
        ILoggingService log,
        IDragBridgeTempFileManager tempManager,
        DragBridgeProcessLauncher processLauncher,
        DragBridgeCopyFlow copyFlow,
        DragBridgePasteHandler pasteHandler,
        ISessionSaver sessionSaver,
        IUiThreadInvoker uiThreadInvoker,
        IQuickAccessPinService quickAccessPinService)
    {
        _sessionSaver = sessionSaver;
        _hotkeyService = hotkeyService;
        _windowOwnerDetector = windowOwnerDetector;
        _notifications = notifications;
        _log = log;
        _uiThreadInvoker = uiThreadInvoker;
        _tempManager = tempManager;
        _processLauncher = processLauncher;
        _copyFlow = copyFlow;
        _pasteHandler = pasteHandler;
        _quickAccessPinService = quickAccessPinService;
    }

    public void Initialize()
    {
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        // Record traverse grants made by the temp file manager into the database
        _tempManager.TraverseGranted += (sid, appliedPaths) =>
        {
            var db = _database;
            if (db == null)
                return;
            var tempRoot = Path.Combine(Constants.ProgramDataDir, Constants.DragBridgeTempDir);
            var entries = TraversePathsHelper.GetOrCreateTraversePaths(db, sid);
            TraversePathsHelper.TrackPath(entries, tempRoot, appliedPaths);
            // Marshal to UI thread — SaveConfig calls ProtectedBuffer.Unprotect which is not thread-safe.
            _uiThreadInvoker.Invoke(() => _sessionSaver.SaveConfig());
        };

        // Best-effort startup cleanup of orphaned temp folders from previous sessions
        _log.Info("DragBridgeService: cleaning up orphaned temp folders in background.");
        _ = Task.Run(() =>
        {
            _tempManager.CleanupOldFolders(TimeSpan.FromHours(24));
            _log.Info("DragBridgeService: orphaned temp folder cleanup complete.");
        });
    }

    public void SetData(SessionContext session)
    {
        _database = session.Database;
        _processLauncher.SetData(session);
    }

    public void ApplySettings(AppSettings settings)
    {
        _copyHotkey = settings.DragBridgeCopyHotkey;

        _hotkeyService.UnregisterAll();
        _enabled = settings.EnableDragBridge;

        if (_enabled)
            RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        if (!_hotkeyService.Register(CopyHotkeyId,
                DragBridgeHotkeyHelper.SplitModifiers(_copyHotkey),
                DragBridgeHotkeyHelper.GetVirtualKey(_copyHotkey)))
            _notifications.ShowWarning("Drag Bridge", "Could not register hotkey — already in use.");
    }

    private void OnHotkeyPressed(int id)
    {
        if (id != CopyHotkeyId)
            return;

        // Capture the foreground window immediately — before any focus changes caused by
        // DragBridge launch or ForceToForeground. This is the window to restore on close.
        var restoreHwnd = NativeInterop.GetForegroundWindow();

        var nowTicks = Environment.TickCount64;
        if (_lastHotkeyTicks.HasValue && nowTicks - _lastHotkeyTicks.Value < 1000)
        {
            if (!_rateLimitNotified)
            {
                _rateLimitNotified = true; // intentionally never reset — one notification per session, subsequent rate-limit hits are silently logged
                _notifications.ShowWarning("Drag Bridge", "Hotkey triggered too quickly — please wait a moment and try again.");
            }
            else
            {
                _log.Info("DragBridgeService: hotkey suppressed by rate limit.");
            }

            return;
        }

        _lastHotkeyTicks = nowTicks;

        StartBridgeAsync(restoreHwnd);
    }

    private void StartBridgeAsync(nint restoreHwnd)
    {
        var (captured, sourceSid, _) = _copyFlow.GetCapturedFiles();
        bool hasCapture = captured is { Count: > 0 };

        var ownerInfo = hasCapture
            ? _windowOwnerDetector.GetForegroundWindowOwnerInfo()
            : _windowOwnerDetector.GetDragSourceOrForegroundOwnerInfo();
        if (ownerInfo == null)
        {
            _notifications.ShowWarning("Drag Bridge", "Cannot identify window owner.");
            return;
        }

        Cursor.Current = Cursors.WaitCursor;
        _processLauncher.KillActiveOperation();
        var cts = _processLauncher.BeginOperation();
        var capturedPaths = captured?.ToList() ?? [];

        _ = Task.Run(() => RunBridgeFlowAsync(ownerInfo.Value, capturedPaths, sourceSid, restoreHwnd, cts.Token), cts.Token)
            .ContinueWith(_ => RestoreCursor(), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task RunBridgeFlowAsync(WindowOwnerInfo ownerInfo, List<string> capturedFiles,
        string? sourceSid, nint restoreHwnd, CancellationToken ct)
    {
        if (_database == null)
            return;

        // Provide resolution delegate — called only when user tries to drag (lazy)
        Func<CancellationToken, Task<List<string>?>>? resolveDelegate = null;
        if (capturedFiles.Count > 0 && sourceSid != null)
        {
            resolveDelegate = async resolveCt =>
            {
                var r = await _pasteHandler.ResolveFileAccessAsync(
                    ownerInfo.Sid, capturedFiles, sourceSid, _database.SidNames, resolveCt);
                if (r.DatabaseModified)
                    _uiThreadInvoker.Invoke(() =>
                    {
                        _sessionSaver.SaveConfig();
                        if (r.GrantedPaths.Count > 0)
                            _quickAccessPinService.PinFolders(ownerInfo.Sid.Value, r.GrantedPaths);
                    });
                return r.Paths;
            };
        }

        var cursorPos = Cursor.Position;
        await _copyFlow.RunBridgeAsync(ownerInfo, capturedFiles, cursorPos, resolveDelegate, restoreHwnd, ct);
    }

    private static void RestoreCursor() => Cursor.Current = Cursors.Default;

    public void Dispose()
    {
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyService.UnregisterAll();
        _processLauncher.KillActiveOperation();
        _tempManager.CleanupOldFolders(TimeSpan.FromHours(24));
    }
}
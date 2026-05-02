using System.IO.Pipes;
using RunFence.Core;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.DragBridge;

/// <summary>
/// Unified DragBridge window: 64×64 DPI-aware drop target and drag source.
/// Green (down-arrow) = ready to receive dropped files.
/// Blue (file/folder icon) = has files ready to drag to target app.
/// Dropping captures files and immediately closes this window.
/// Dragging delivers files and immediately closes this window; if files are unresolved
/// (cross-user), sends a ResolveRequest and waits for the main app to resolve access
/// — window stays open for retry. Dragging back onto self is a no-op.
/// Click without action = cancel. Auto-closes after 30 seconds of inactivity.
/// </summary>
public class DragBridgeWindow : Form
{
    private readonly int _cursorX, _cursorY;
    private readonly Timer _autoCloseTimer;
    private readonly Timer _topmostTimer;
    private readonly ToolTip _toolTip;
    private readonly DragBridgePipeClient _pipeClient;

    private List<string>? _filePaths; // null = connecting; [] = empty/receive; [...] = has files
    private bool _filesResolved = true; // true = safe to drag; false = need resolve request first
    private bool _resolvePending; // prevents duplicate resolve requests

    private readonly int _runFencePid;
    private readonly nint _restoreHwnd; // window active at hotkey time; fallback when _previousForegroundWindow is 0
    private nint _previousForegroundWindow;
    private bool _hasActiveFocus;

    private bool _mouseDownInWindow;
    private MouseButtons _mouseDownButton;
    private Point _mouseDownPoint;
    private bool _isDragging; // true while DoDragDrop is executing
    private bool _droppedOnSelf; // set in OnDragDrop when a self-drop is detected

    public DragBridgeWindow(NamedPipeClientStream pipe, int cursorX, int cursorY, int runFencePid = 0, nint restoreHwnd = 0)
    {
        _cursorX = cursorX;
        _cursorY = cursorY;
        _runFencePid = runFencePid;
        _restoreHwnd = restoreHwnd;

        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        AllowDrop = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0, 160, 80); // green until state known

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Click += (_, _) =>
        {
            if (_filePaths == null || _filePaths.Count == 0)
                Close();
        };
        Paint += OnPaint;

        _autoCloseTimer = new Timer { Interval = 30_000 };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();

        _topmostTimer = new Timer { Interval = 200 };
        _topmostTimer.Tick += (_, _) => WindowNative.SetWindowPos(Handle, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_NOACTIVATE);

        _toolTip = new ToolTip { AutoPopDelay = 10_000, InitialDelay = 400 };
        UpdateTooltip();

        _pipeClient = new DragBridgePipeClient(pipe, uiInvoker: this);
        _pipeClient.InitialFilesReceived += (files, resolved) =>
        {
            _filePaths = files;
            // Empty = drop mode (no files yet), pre-resolved = main app confirmed access.
            // Otherwise start unresolved so the first drag triggers a resolve request.
            _filesResolved = files.Count == 0 || resolved;
            UpdateBackColor();
            UpdateTooltip();
            ResetAutoCloseTimer();
            Invalidate();
        };
        _pipeClient.ResolveSucceeded += files =>
        {
            _filePaths = files;
            _filesResolved = true;
            UpdateBackColor();
            UpdateTooltip();
            ResetAutoCloseTimer();
            Invalidate();
        };
        _pipeClient.ResolveCancelled += Close;
        _pipeClient.DropSent += files =>
        {
            _filePaths = files;
            _filesResolved = true; // dropped by window user — no resolution needed
            UpdateBackColor();
            UpdateTooltip();
            Invalidate();
            Close(); // close immediately after files are received
        };
        _pipeClient.ResolvePendingCleared += () => _resolvePending = false;

        _ = _pipeClient.ReceiveAndRunAsync();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var scale = DeviceDpi / 96.0;
        var size = (int)(64 * scale);
        ClientSize = new Size(size, size);
        Location = new Point(_cursorX - size / 2, _cursorY - size / 2);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        WindowNative.SetWindowPos(Handle, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE);
        WindowForegroundHelper.ForceToForeground(Handle);
        _topmostTimer.Start();
    }

    // --- Drop handling ---

    private void OnDragEnter(object? sender, DragEventArgs e)
        => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        if (_isDragging)
        {
            // Files were dragged from this window and dropped back onto it — ignore.
            _droppedOnSelf = true;
            return;
        }

        _ = _pipeClient.SendDropAsync([.. files]);
    }

    // --- Mouse drag (send-only when has files; both buttons; resolution-aware) ---

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_filePaths == null || _filePaths.Count == 0)
            return;
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            _mouseDownInWindow = true;
            _mouseDownButton = e.Button;
            _mouseDownPoint = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_mouseDownInWindow || (e.Button & _mouseDownButton) == MouseButtons.None)
            return;
        if (_filePaths == null || _filePaths.Count == 0)
            return;

        var dragSize = SystemInformation.DragSize;
        var delta = new Size(Math.Abs(e.X - _mouseDownPoint.X), Math.Abs(e.Y - _mouseDownPoint.Y));
        if (delta.Width <= dragSize.Width && delta.Height <= dragSize.Height)
            return;

        _mouseDownInWindow = false;

        if (!_filesResolved)
        {
            // Request resolution from main app; cancel drag; window stays open for retry
            if (!_resolvePending)
            {
                _resolvePending = true;
                ResetAutoCloseTimer();
                _ = _pipeClient.SendResolveRequestAsync();
            }

            return;
        }

        // Files resolved — deliver
        var dataObj = new DataObject(DataFormats.FileDrop, _filePaths.ToArray());
        _isDragging = true;
        DoDragDrop(dataObj, DragDropEffects.Copy);
        _isDragging = false;
        if (!_droppedOnSelf)
            Close();
        _droppedOnSelf = false;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_filePaths == null || _filePaths.Count == 0)
            return;
        if (e.Button == _mouseDownButton && _mouseDownInWindow)
        {
            _mouseDownInWindow = false;
            Close(); // click without drag = cancel
        }
    }

    // --- Visual state ---

    private void UpdateBackColor()
    {
        BackColor = _filePaths is { Count: > 0 }
            ? Color.FromArgb(0, 100, 200)
            : Color.FromArgb(0, 160, 80);
    }

    private void UpdateTooltip()
    {
        _toolTip.SetToolTip(this, _filePaths is { Count: > 0 }
            ? "Drag from here to deliver files. Right-click drag supported. Click to cancel."
            : "Drop files here to bridge them across accounts. Click to cancel.");
    }

    private void ResetAutoCloseTimer()
    {
        _autoCloseTimer.Stop();
        _autoCloseTimer.Start();
    }

    // --- Painting ---

    private void OnPaint(object? sender, PaintEventArgs e) =>
        DragBridgeIconRenderer.Paint(e.Graphics, ClientRectangle, _filePaths);

    private bool BelongsToRunFence(nint hwnd)
    {
        if (_runFencePid == 0 || hwnd == 0)
            return false;
        WindowNative.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)_runFencePid;
    }

    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        // Restore focus before the window is destroyed. Must happen in OnFormClosing (not OnFormClosed)
        // — by OnFormClosed, Windows has already transferred foreground and SetForegroundWindow is
        // silently ignored. Plain SetForegroundWindow works here because DragBridge is still the
        // foreground process at this point.
        // Use the WM_ACTIVATE-tracked window if available; fall back to the hotkey-time capture
        // (_restoreHwnd) when WM_ACTIVATE only saw RunFence windows (filtered to 0).
        var hwndToRestore = _previousForegroundWindow != 0 ? _previousForegroundWindow
            : !BelongsToRunFence(_restoreHwnd) ? _restoreHwnd : 0;
        if (_hasActiveFocus && hwndToRestore != 0)
            WindowNative.SetForegroundWindow(hwndToRestore);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                // Don't activate DragBridge when the user clicks it while another window is active.
                // This keeps the current foreground window (e.g. the app the user just switched to)
                // focused: _hasActiveFocus stays false → OnFormClosing skips SetForegroundWindow →
                // the OS leaves whatever window was active before the click still in the foreground.
                m.Result = MA_NOACTIVATE;
                return;
            case WM_ACTIVATE when (m.WParam.ToInt32() & 0xFFFF) != WA_INACTIVE:
            {
                // Gaining focus: lParam is the handle of the window losing activation.
                // Skip RunFence's own windows — they appear between the user's real target window
                // and DragBridge when DragBridge is clicked (e.g. user briefly visited RunFence's
                // main form before clicking DragBridge to close it). Keeping the last non-RunFence
                // window ensures we restore the source/target the user was working with.
                if (m.LParam != 0 && !BelongsToRunFence(m.LParam))
                    _previousForegroundWindow = m.LParam;
                _hasActiveFocus = true;
                break;
            }
            case WM_ACTIVATE:
                _hasActiveFocus = false;
                break;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Dispose();
            _topmostTimer.Stop();
            _topmostTimer.Dispose();
            _toolTip.Dispose();
            _pipeClient.Dispose();
        }

        base.Dispose(disposing);
    }
}

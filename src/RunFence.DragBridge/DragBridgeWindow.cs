using System.Drawing.Drawing2D;
using System.IO.Pipes;
using RunFence.Core;
using RunFence.Core.Ipc;
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
    private readonly NamedPipeClientStream? _pipe;

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

    private enum IconKind
    {
        SingleFile,
        MultiFile,
        SingleFolder,
        MultiFolder
    }

    public DragBridgeWindow(NamedPipeClientStream pipe, int cursorX, int cursorY, int runFencePid = 0, nint restoreHwnd = 0)
    {
        _pipe = pipe;
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
        _topmostTimer.Tick += (_, _) => NativeInterop.SetWindowPos(Handle, NativeInterop.HWND_TOPMOST, 0, 0, 0, 0,
            NativeInterop.SWP_NOMOVE | NativeInterop.SWP_NOSIZE | NativeInterop.SWP_NOACTIVATE);

        _toolTip = new ToolTip { AutoPopDelay = 10_000, InitialDelay = 400 };
        UpdateTooltip();

        _ = ReceiveAndRunAsync();
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
        NativeInterop.SetWindowPos(Handle, NativeInterop.HWND_TOPMOST, 0, 0, 0, 0,
            NativeInterop.SWP_NOMOVE | NativeInterop.SWP_NOSIZE);
        WindowForegroundHelper.ForceToForeground(Handle);
        _topmostTimer.Start();
    }

    private async Task ReceiveAndRunAsync()
    {
        try
        {
            // Read initial file list from main app (pipe already connected)
            var initial = await DragBridgeProtocol.ReadAsync(_pipe!);
            var initialFiles = initial?.FilePaths ?? [];
            if (!IsDisposed)
                BeginInvoke(() =>
                {
                    _filePaths = initialFiles;
                    _filesResolved = initialFiles.Count == 0; // cross-user files start unresolved
                    UpdateBackColor();
                    UpdateTooltip();
                    ResetAutoCloseTimer();
                    Invalidate();
                });

            // Background loop: read resolve responses from main app
            while (true)
            {
                var response = await DragBridgeProtocol.ReadAsync(_pipe!);
                if (response == null)
                    break; // main app closed pipe
                if (!IsDisposed)
                    BeginInvoke(() =>
                    {
                        _resolvePending = false;
                        if (response.FilePaths.Count > 0)
                        {
                            // Resolution succeeded — update files, now safe to drag
                            _filePaths = response.FilePaths;
                            _filesResolved = true;
                        }

                        // else: cancelled — keep current files, _filesResolved stays false
                        UpdateBackColor();
                        UpdateTooltip();
                        ResetAutoCloseTimer();
                        Invalidate();
                    });
            }
        }
        catch (IOException)
        {
        } // main app closed pipe — normal
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            if (!IsDisposed)
                BeginInvoke(Close);
        }
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

        _ = SendDropAsync([.. files]);
    }

    private async Task SendDropAsync(List<string> files)
    {
        try
        {
            if (_pipe?.IsConnected == true)
                await DragBridgeProtocol.WriteAsync(_pipe,
                    new DragBridgeData { FilePaths = files }); // MessageType=FileList (default)
        }
        catch
        {
        }

        if (!IsDisposed)
            BeginInvoke(() =>
            {
                _filePaths = files;
                _filesResolved = true; // dropped by window user — no resolution needed
                UpdateBackColor();
                UpdateTooltip();
                Invalidate();
                Close(); // close immediately after files are received
            });
    }

    private async Task SendResolveRequestAsync()
    {
        try
        {
            if (_pipe?.IsConnected == true)
                await DragBridgeProtocol.WriteAsync(_pipe,
                    new DragBridgeData { MessageType = DragBridgeMessageType.ResolveRequest });
        }
        catch
        {
            if (!IsDisposed)
                BeginInvoke(() => _resolvePending = false);
        }
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
                _ = SendResolveRequestAsync();
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

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        int cx = r.Width / 2, cy = r.Height / 2;

        if (_filePaths is { Count: > 0 })
        {
            using var brush = new SolidBrush(Color.FromArgb(0, 100, 200));
            g.FillEllipse(brush, 2, 2, r.Width - 4, r.Height - 4);

            using var iconBrush = new SolidBrush(Color.White);
            DrawContentIcon(g, iconBrush, cx, cy, DetermineIconKind(_filePaths));
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(0, 160, 80));
            g.FillEllipse(brush, 2, 2, r.Width - 4, r.Height - 4);

            using var arrowBrush = new SolidBrush(Color.White);
            var arrowPts = new[]
            {
                new Point(cx, cy + 14),
                new Point(cx - 10, cy),
                new Point(cx - 4, cy),
                new Point(cx - 4, cy - 14),
                new Point(cx + 4, cy - 14),
                new Point(cx + 4, cy),
                new Point(cx + 10, cy)
            };
            g.FillPolygon(arrowBrush, arrowPts);
        }
    }

    private static void DrawContentIcon(Graphics g, SolidBrush brush, int cx, int cy, IconKind kind)
    {
        switch (kind)
        {
            case IconKind.MultiFile:
                DrawPageShape(g, brush, cx + 4, cy - 4);
                DrawPageShape(g, brush, cx, cy);
                break;
            case IconKind.SingleFolder:
                DrawFolderShape(g, brush, cx, cy);
                break;
            case IconKind.MultiFolder:
                DrawFolderShape(g, brush, cx + 4, cy - 3);
                DrawFolderShape(g, brush, cx, cy);
                break;
            default: // SingleFile
                DrawPageShape(g, brush, cx, cy);
                break;
        }
    }

    private static void DrawPageShape(Graphics g, SolidBrush brush, float dx, float dy)
    {
        // Rectangle with folded top-right corner: L-shaped notch exposes blue background as the fold
        var pts = new PointF[]
        {
            new(dx - 7, dy - 12), // TL
            new(dx + 4, dy - 12), // fold start (top)
            new(dx + 4, dy - 7), // fold inner corner
            new(dx + 8, dy - 7), // fold outer corner
            new(dx + 8, dy + 12), // BR
            new(dx - 7, dy + 12), // BL
        };
        g.FillPolygon(brush, pts);
    }

    private static void DrawFolderShape(Graphics g, SolidBrush brush, float dx, float dy)
    {
        // Tab (top part of folder)
        g.FillRectangle(brush, dx - 9, dy - 13, 8, 4);
        // Body
        g.FillRectangle(brush, dx - 9, dy - 9, 18, 20);
    }

    private static IconKind DetermineIconKind(List<string> paths)
    {
        bool allFolders = paths.All(Directory.Exists);
        bool anyFolder = paths.Any(Directory.Exists);
        return (anyFolder, allFolders, paths.Count > 1) switch
        {
            (false, _, false) => IconKind.SingleFile,
            (false, _, true) => IconKind.MultiFile,
            (true, true, false) => IconKind.SingleFolder,
            _ => paths.Count > 1 && allFolders ? IconKind.MultiFolder : IconKind.MultiFile,
        };
    }

    private bool BelongsToRunFence(nint hwnd)
    {
        if (_runFencePid == 0 || hwnd == 0)
            return false;
        NativeInterop.GetWindowThreadProcessId(hwnd, out var pid);
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
            NativeInterop.SetForegroundWindow(hwndToRestore);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            // Don't activate DragBridge when the user clicks it while another window is active.
            // This keeps the current foreground window (e.g. the app the user just switched to)
            // focused: _hasActiveFocus stays false → OnFormClosing skips SetForegroundWindow →
            // the OS leaves whatever window was active before the click still in the foreground.
            m.Result = MA_NOACTIVATE;
            return;
        }

        if (m.Msg == WM_ACTIVATE)
        {
            if ((m.WParam.ToInt32() & 0xFFFF) != WA_INACTIVE)
            {
                // Gaining focus: lParam is the handle of the window losing activation.
                // Skip RunFence's own windows — they appear between the user's real target window
                // and DragBridge when DragBridge is clicked (e.g. user briefly visited RunFence's
                // main form before clicking DragBridge to close it). Keeping the last non-RunFence
                // window ensures we restore the source/target the user was working with.
                if (m.LParam != 0 && !BelongsToRunFence(m.LParam))
                    _previousForegroundWindow = m.LParam;
                _hasActiveFocus = true;
            }
            else
            {
                _hasActiveFocus = false;
            }
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
            _pipe?.Dispose();
        }

        base.Dispose(disposing);
    }
}
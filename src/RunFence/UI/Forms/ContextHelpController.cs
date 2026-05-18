namespace RunFence.UI.Forms;

public sealed class ContextHelpController : IMessageFilter, IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private readonly ContextHelpForm _form;
    private readonly ContextHelpButton _button;
    private readonly ContextHelpOverlay _overlay;
    private readonly ContextHelpModeCoordinator _modeCoordinator;
    private readonly Action _removeMessageFilter;
    private bool _disposed;

    public ContextHelpController(
        ContextHelpForm form,
        ContextHelpButton button,
        ContextHelpOverlay overlay,
        ContextHelpModeCoordinator modeCoordinator,
        Action removeMessageFilter)
    {
        _form = form;
        _button = button;
        _overlay = overlay;
        _modeCoordinator = modeCoordinator;
        _removeMessageFilter = removeMessageFilter;

        _button.MouseDown += OnButtonMouseDown;
        _button.MouseUp += OnButtonMouseUp;
        _button.MouseMove += OnButtonMouseMove;
        _button.MouseEnter += OnButtonMouseHoverChanged;
        _button.MouseLeave += OnButtonMouseHoverChanged;
        _form.Deactivate += OnFormDeactivate;
        _form.VisibleChanged += OnFormVisibleChanged;
        _form.LocationChanged += OnFormBoundsChanged;
        _form.SizeChanged += OnFormBoundsChanged;
        _overlay.MouseDown += OnOverlayMouseDown;
        _overlay.MouseMove += OnOverlayMouseMove;
        _overlay.MouseUp += OnOverlayMouseUp;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (_disposed)
            return false;

        _modeCoordinator.HandlePopupDismissMessage(m);

        if (IsEscapeForForm(m))
            return _modeCoordinator.HandleEscape();

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _button.MouseDown -= OnButtonMouseDown;
        _button.MouseUp -= OnButtonMouseUp;
        _button.MouseMove -= OnButtonMouseMove;
        _button.MouseEnter -= OnButtonMouseHoverChanged;
        _button.MouseLeave -= OnButtonMouseHoverChanged;
        _form.Deactivate -= OnFormDeactivate;
        _form.VisibleChanged -= OnFormVisibleChanged;
        _form.LocationChanged -= OnFormBoundsChanged;
        _form.SizeChanged -= OnFormBoundsChanged;
        _overlay.MouseDown -= OnOverlayMouseDown;
        _overlay.MouseMove -= OnOverlayMouseMove;
        _overlay.MouseUp -= OnOverlayMouseUp;
        _modeCoordinator.Dispose();
        _removeMessageFilter();
    }

    private void OnButtonMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _disposed)
            return;

        if (_modeCoordinator.IsHelpModeActive)
        {
            if (!_modeCoordinator.IsMouseSelectionInProgress)
                _modeCoordinator.ExitHelpMode(showInstructionsOnButton: true);

            return;
        }

        var selectionStartScreenPoint = _button.PointToScreen(e.Location);
        _modeCoordinator.EnterHelpModeFromButton(selectionStartScreenPoint);
        _overlay.Capture = true;
        _modeCoordinator.UpdateButtonSelection(selectionStartScreenPoint);
    }

    private void OnButtonMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_modeCoordinator.IsHelpModeActive || !_modeCoordinator.IsMouseSelectionInProgress)
            return;

        var screenPoint = _button.PointToScreen(e.Location);
        _modeCoordinator.CompleteSelection(screenPoint);
    }

    private void OnButtonMouseMove(object? sender, MouseEventArgs e)
    {
        var screenPoint = _button.PointToScreen(e.Location);
        _modeCoordinator.UpdateButtonSelection(screenPoint);
    }

    private void OnButtonMouseHoverChanged(object? sender, EventArgs e)
    {
        _modeCoordinator.UpdateButtonSelection(Control.MousePosition);
    }

    private void OnFormDeactivate(object? sender, EventArgs e)
    {
        if (_modeCoordinator.IsHelpModeActive)
            _modeCoordinator.ExitHelpMode();
        else
            _modeCoordinator.DismissPopup();
    }

    private void OnFormVisibleChanged(object? sender, EventArgs e)
    {
        if (_form.Visible)
            return;

        if (_modeCoordinator.IsHelpModeActive)
            _modeCoordinator.ExitHelpMode();
        else
            _modeCoordinator.DismissPopup();
    }

    private void OnFormBoundsChanged(object? sender, EventArgs e)
    {
        _modeCoordinator.RefreshOverlayBounds();
    }

    private void OnOverlayMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right or MouseButtons.Middle))
            return;

        _modeCoordinator.BeginOverlaySelection(e.Location);
    }

    private void OnOverlayMouseMove(object? sender, MouseEventArgs e)
    {
        _modeCoordinator.UpdateOverlaySelection(e.Location);
    }

    private void OnOverlayMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_modeCoordinator.IsMouseSelectionInProgress)
            return;

        var screenPoint = _overlay.PointToScreen(e.Location);
        _modeCoordinator.CompleteSelection(screenPoint);
    }

    private bool IsEscapeForForm(Message m)
    {
        if (m.Msg is not (WmKeyDown or WmSysKeyDown))
            return false;

        if ((Keys)(nint)m.WParam != Keys.Escape)
            return false;

        if (Form.ActiveForm == _form)
            return true;

        var sourceControl = Control.FromHandle(m.HWnd);
        return sourceControl?.FindForm() == _form;
    }
}

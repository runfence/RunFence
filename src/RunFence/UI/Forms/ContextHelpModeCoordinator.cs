namespace RunFence.UI.Forms;

public sealed class ContextHelpModeCoordinator : IDisposable
{
    private readonly ContextHelpForm _form;
    private readonly ContextHelpButton _button;
    private readonly ContextHelpOverlay _overlay;
    private readonly ContextHelpTargetResolver _targetResolver;
    private readonly ContextHelpSnapshotRenderer _snapshotRenderer;
    private readonly ContextHelpPopupPresenter _popupPresenter;
    private readonly ContextHelpSelectionSession _selectionSession;
    private readonly Action _ensureMessageFilterInstalled;
    private readonly Action _removeMessageFilter;

    public ContextHelpModeCoordinator(
        ContextHelpForm form,
        ContextHelpButton button,
        ContextHelpOverlay overlay,
        ContextHelpTargetResolver targetResolver,
        ContextHelpSnapshotRenderer snapshotRenderer,
        ContextHelpPopupPresenter popupPresenter,
        ContextHelpSelectionSession selectionSession,
        Action ensureMessageFilterInstalled,
        Action removeMessageFilter)
    {
        _form = form;
        _button = button;
        _overlay = overlay;
        _targetResolver = targetResolver;
        _snapshotRenderer = snapshotRenderer;
        _popupPresenter = popupPresenter;
        _selectionSession = selectionSession;
        _ensureMessageFilterInstalled = ensureMessageFilterInstalled;
        _removeMessageFilter = removeMessageFilter;
    }

    public bool IsHelpModeActive => _selectionSession.IsActive;
    public bool IsMouseSelectionInProgress => _selectionSession.MouseSelectionInProgress;

    public void EnterHelpModeFromButton(Point selectionStartScreenPoint)
    {
        if (_selectionSession.IsActive)
            return;

        _selectionSession.EnterFromButton(selectionStartScreenPoint);
        _ensureMessageFilterInstalled();
        DismissPopup();
        ShowOverlay();
    }

    public void ExitHelpMode(bool showInstructionsOnButton = false)
    {
        if (_selectionSession.IsActive)
        {
            _selectionSession.Exit();
            _overlay.Capture = false;
            HideOverlay();
        }

        if (showInstructionsOnButton)
        {
            ShowPopup(_button, ContextHelpTextResolver.InstructionText);
            return;
        }

        DismissPopup();
    }

    public void DismissPopup()
    {
        _popupPresenter.Hide();
        if (!_selectionSession.IsActive)
            _removeMessageFilter();
    }

    public bool HandlePopupDismissMessage(Message message)
    {
        if (!_popupPresenter.IsPopupDismissMessage(message))
            return false;

        DismissPopup();
        return true;
    }

    public bool HandleEscape()
    {
        if (_popupPresenter.HasVisiblePopup)
        {
            DismissPopup();
            return true;
        }

        if (!_selectionSession.IsActive)
            return false;

        ExitHelpMode();
        return true;
    }

    public void RefreshOverlayBounds()
    {
        if (_selectionSession.IsActive)
        {
            _overlay.Bounds = _form.ClientRectangle;
            _overlay.SetBackgroundSnapshot(CaptureFormSnapshot());
            RefreshOverlayHighlights(Control.MousePosition);
            return;
        }

        DismissPopup();
    }

    public void BeginOverlaySelection(Point overlayPoint)
    {
        _selectionSession.BeginOverlaySelection();
        _overlay.Capture = true;
        UpdateOverlayHover(overlayPoint);
    }

    public void UpdateButtonSelection(Point screenPoint)
    {
        if (!_selectionSession.IsActive)
            return;

        _selectionSession.UpdateButtonDragState(screenPoint);
        RefreshOverlayHighlights(screenPoint);
    }

    public void UpdateOverlaySelection(Point overlayPoint)
    {
        _selectionSession.UpdateButtonDragState(_overlay.PointToScreen(overlayPoint));
        UpdateOverlayHover(overlayPoint);
    }

    public void CompleteSelection(Point screenPoint)
    {
        _overlay.Capture = false;

        if (_selectionSession.CompleteMouseSelection(screenPoint))
        {
            RefreshOverlayHighlights(screenPoint);
            return;
        }

        CompleteSelectionAt(screenPoint);
    }

    public void Dispose()
    {
        DismissPopup();
        HideOverlay();
        _overlay.Dispose();
        _popupPresenter.Dispose();
    }

    private void CompleteSelectionAt(Point screenPoint)
    {
        var target = _targetResolver.ResolveHitTarget(screenPoint);
        if (target == null)
        {
            ExitHelpMode();
            return;
        }

        ExitHelpMode(target.ShowInstructionsOnButton);
        if (!target.ShowInstructionsOnButton)
            ShowPopup(target.AnchorControl, target.AnchorPoint, target.HelpText);
    }

    private void ShowPopup(Control target, string text)
    {
        _ensureMessageFilterInstalled();
        _popupPresenter.Show(target, text);
    }

    private void ShowPopup(Control target, Point anchorPoint, string text)
    {
        _ensureMessageFilterInstalled();
        _popupPresenter.Show(target, anchorPoint, text);
    }

    private void ShowOverlay()
    {
        var snapshot = CaptureFormSnapshot();
        _overlay.Bounds = _form.ClientRectangle;
        if (_overlay.Parent != _form)
        {
            _form.Controls.Add(_overlay);
            _overlay.BringToFront();
        }

        _overlay.SetBackgroundSnapshot(snapshot);
        RefreshOverlayHighlights(Control.MousePosition);
    }

    private void HideOverlay()
    {
        _overlay.SetHighlights([], null);
        _overlay.SetBackgroundSnapshot(null);
        if (_overlay.Parent == _form)
            _form.Controls.Remove(_overlay);
    }

    private void RefreshOverlayHighlights(Point screenPoint)
    {
        var highlightTargets = _targetResolver.GetHighlightTargets();
        var highlights = highlightTargets.Select(static target => target.Rect).ToList();
        var hoverHighlight = _targetResolver.TryGetHighlightAt(highlightTargets, screenPoint)?.Rect;

        _overlay.SetHighlights(highlights, hoverHighlight);
    }

    private void UpdateOverlayHover(Point overlayPoint)
    {
        RefreshOverlayHighlights(_overlay.PointToScreen(overlayPoint));
    }

    private Bitmap? CaptureFormSnapshot() =>
        _snapshotRenderer.CaptureFormSnapshot(
            _form,
            _button,
            _overlay,
            _form.GetContextHelpSnapshotParticipants());
}

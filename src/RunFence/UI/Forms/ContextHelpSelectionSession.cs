namespace RunFence.UI.Forms;

public sealed class ContextHelpSelectionSession
{
    private readonly ContextHelpForm _form;
    private readonly ContextHelpButton _button;
    private bool _selectionStartedFromButton;
    private bool _selectionDraggedAwayFromButton;
    private Point _selectionStartScreenPoint;

    public ContextHelpSelectionSession(ContextHelpForm form, ContextHelpButton button)
    {
        _form = form;
        _button = button;
    }

    public bool IsActive { get; private set; }
    public bool MouseSelectionInProgress { get; private set; }

    public void EnterFromButton(Point selectionStartScreenPoint)
    {
        IsActive = true;
        MouseSelectionInProgress = true;
        _selectionStartedFromButton = true;
        _selectionDraggedAwayFromButton = false;
        _selectionStartScreenPoint = selectionStartScreenPoint;
    }

    public void Exit()
    {
        IsActive = false;
        MouseSelectionInProgress = false;
        _selectionStartedFromButton = false;
        _selectionDraggedAwayFromButton = false;
    }

    public void BeginOverlaySelection()
    {
        if (!IsActive)
            return;

        MouseSelectionInProgress = true;
        _selectionStartedFromButton = false;
        _selectionDraggedAwayFromButton = false;
    }

    public void UpdateButtonDragState(Point screenPoint)
    {
        if (!MouseSelectionInProgress || !_selectionStartedFromButton || _selectionDraggedAwayFromButton)
            return;

        var dragThreshold = Math.Max(4, _form.ScaleHelpLogicalPixels(4));
        if (Math.Abs(screenPoint.X - _selectionStartScreenPoint.X) >= dragThreshold ||
            Math.Abs(screenPoint.Y - _selectionStartScreenPoint.Y) >= dragThreshold ||
            !IsPointOverButton(screenPoint))
        {
            _selectionDraggedAwayFromButton = true;
        }
    }

    public bool ShouldKeepHelpModeActive(Point screenPoint)
    {
        return _selectionStartedFromButton &&
            !_selectionDraggedAwayFromButton &&
            IsPointOverButton(screenPoint);
    }

    public bool CompleteMouseSelection(Point screenPoint)
    {
        if (!MouseSelectionInProgress)
            return false;

        MouseSelectionInProgress = false;
        var keepHelpModeActive = ShouldKeepHelpModeActive(screenPoint);
        _selectionStartedFromButton = false;
        _selectionDraggedAwayFromButton = false;
        return keepHelpModeActive;
    }

    private bool IsPointOverButton(Point screenPoint) =>
        _button.RectangleToScreen(_button.ClientRectangle).Contains(screenPoint);
}

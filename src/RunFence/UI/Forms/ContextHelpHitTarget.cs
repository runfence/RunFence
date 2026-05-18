namespace RunFence.UI.Forms;

public sealed class ContextHelpHitTarget
{
    public ContextHelpHitTarget(Control anchorControl, Point anchorPoint, string helpText, bool showInstructionsOnButton = false)
    {
        AnchorControl = anchorControl;
        AnchorPoint = anchorPoint;
        HelpText = helpText;
        ShowInstructionsOnButton = showInstructionsOnButton;
    }

    public Control AnchorControl { get; }
    public Point AnchorPoint { get; }
    public string HelpText { get; }
    public bool ShowInstructionsOnButton { get; }
}

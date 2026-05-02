namespace RunFence.Infrastructure;

public sealed class ClipboardPasteTargetResolution
{
    private ClipboardPasteTargetResolution(bool shouldIntercept, bool passThrough, ClipboardPasteTarget target, string? failureMessage)
    {
        ShouldIntercept = shouldIntercept;
        PassThrough = passThrough;
        Target = target;
        FailureMessage = failureMessage;
    }

    public bool ShouldIntercept { get; }
    public bool PassThrough { get; }
    public ClipboardPasteTarget Target { get; }
    public string? FailureMessage { get; }

    public static ClipboardPasteTargetResolution Intercept(ClipboardPasteTarget target) =>
        new(true, false, target, null);

    public static ClipboardPasteTargetResolution Passthrough(string? failureMessage = null) =>
        new(false, true, default, failureMessage);
}

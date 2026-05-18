namespace RunFence.Launch;

public sealed class LaunchFeedbackContext(string startedItem, LaunchFeedbackSource source)
{
    public string StartedItem { get; } = startedItem;
    public LaunchFeedbackSource Source { get; } = source;
    public string? SummaryName { get; init; }
    public IWin32Window? Owner { get; init; }
    public string? GrantFailureSubject { get; init; }
    public bool UseRunAsGrantFailureWording { get; init; }
    public string WarningCaption { get; init; } = "RunFence";
    public string FailureCaption { get; init; } = "Error";
    public MessageBoxIcon FailureIcon { get; init; } = MessageBoxIcon.Error;
}

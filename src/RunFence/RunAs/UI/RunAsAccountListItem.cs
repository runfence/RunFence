namespace RunFence.RunAs.UI;

public sealed class RunAsAccountListItem(
    object displayItem,
    string displayText,
    RunAsAccountOptionSource? optionSource,
    bool isSeparator)
{
    public object DisplayItem { get; } = displayItem;

    public string DisplayText { get; } = displayText;

    public RunAsAccountOptionSource? OptionSource { get; } = optionSource;

    public bool IsSeparator { get; } = isSeparator;

    public override string ToString() => DisplayText;
}

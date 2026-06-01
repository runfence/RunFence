namespace RunFence.RunAs.UI;

public sealed record CreateAccountRunAsOptionSource(
    int ListIndex,
    string DisplayText) : RunAsAccountOptionSource(ListIndex, DisplayText);

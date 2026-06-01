namespace RunFence.RunAs.UI;

public sealed record CreateContainerRunAsOptionSource(
    int ListIndex,
    string DisplayText) : RunAsAccountOptionSource(ListIndex, DisplayText);

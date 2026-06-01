using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record AppContainerRunAsOptionSource(
    int ListIndex,
    string DisplayText,
    AppContainerEntry Container,
    string ContainerSid) : RunAsAccountOptionSource(ListIndex, DisplayText);

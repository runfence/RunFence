using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record CredentialRunAsOptionSource(
    int ListIndex,
    string DisplayText,
    CredentialEntry Credential) : RunAsAccountOptionSource(ListIndex, DisplayText);

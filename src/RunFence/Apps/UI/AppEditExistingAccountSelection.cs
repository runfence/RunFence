namespace RunFence.Apps.UI;

public sealed record AppEditExistingAccountSelection(
    string AccountSid,
    string? AppContainerName)
{
    public bool IsAppContainer => !string.IsNullOrEmpty(AppContainerName);
}

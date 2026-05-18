namespace RunFence.RunAs.UI;

public sealed record AppEntryPermissionPromptDecision(
    AppEntryPermissionPromptResult Result,
    AppEntryPermissionGrantRequest? GrantRequest = null);

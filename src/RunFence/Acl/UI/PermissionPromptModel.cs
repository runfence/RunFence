namespace RunFence.Acl.UI;

public sealed record PermissionPromptModel(
    string Caption,
    string Heading,
    string BodyText,
    string ConfirmButtonText,
    string SkipButtonText,
    string CancelButtonText);

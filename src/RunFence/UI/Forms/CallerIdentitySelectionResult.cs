namespace RunFence.UI.Forms;

public sealed record CallerIdentitySelectionResult(
    bool Accepted,
    string? SelectedSid,
    string? DisplayName);

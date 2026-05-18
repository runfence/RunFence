namespace RunFence.Groups;

public record GroupDeletionResult(
    GroupDeletionStatus Status,
    string GroupSid,
    string? GroupName,
    IReadOnlyList<string> ChangedAppIds,
    bool DataChangedRaised,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? SaveErrorMessage = null);

namespace RunFence.Infrastructure;

public sealed record GroupMutationResult(
    GroupMutationStatus Status,
    string? GroupSid,
    string? GroupName,
    IReadOnlyList<string>? MemberSids,
    string? Error);

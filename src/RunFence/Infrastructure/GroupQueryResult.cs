using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed record GroupQueryResult(
    GroupQueryStatus Status,
    string? GroupSid,
    string? GroupName,
    IReadOnlyList<string>? MemberSids,
    IReadOnlyList<LocalUserAccount>? Accounts,
    string? Description,
    string? Error);

namespace RunFence.Groups.UI;

public sealed record GroupRefreshCompletedInfo(
    string? SelectedSidBeforeRefresh,
    string? SelectedSidAfterRefresh,
    bool MembersWereRefreshed);

namespace RunFence.Account.UI;

public interface IAccountsPanelLifecycleView
{
    bool Visible { get; }
    bool IsRefreshing { get; }
    bool IsOperationInProgress { get; }
    bool IsSortActive { get; }
    bool IsParentFormVisible();
    Task InitialRefreshAsync();
    Task RefreshGridAsync(CancellationToken cancellationToken = default);
    void ShowSidMigrationWarning();
}

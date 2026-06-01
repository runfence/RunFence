namespace RunFence.Apps.UI;

public interface IApplicationsPanelRefreshView
{
    void SetIsRefreshing(bool isRefreshing);
    void ReapplyGlyphIfActive();
    void UpdateButtonState();
    void SelectAppById(string? appId);
    void SelectRowByIndex(int rowIndex);
    void SelectFirstRow();
    void PublishDataChanged();
}

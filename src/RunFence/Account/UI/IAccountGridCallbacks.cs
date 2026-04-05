namespace RunFence.Account.UI;

/// <summary>
/// Provides UI callbacks that account grid handlers need from <see cref="Forms.AccountsPanel"/>.
/// Implemented by <see cref="Forms.AccountsPanel"/> and passed to handlers via Initialize().
/// </summary>
public interface IAccountGridCallbacks
{
    void ReapplyGlyph();
    void SelectFirstRow();
    void UpdateButtonState();
    void SetIsRefreshing();
    void ClearIsRefreshing();
    void UpdateStatus(string text);
    void ClearStatus();
}
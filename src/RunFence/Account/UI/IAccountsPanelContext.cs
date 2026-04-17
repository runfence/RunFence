using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Provides access to shared state and operations for account panel handlers,
/// replacing 7-8 Func&lt;&gt;/Action&lt;&gt; callback parameters.
/// </summary>
public interface IAccountsPanelContext
{
    AppDatabase Database { get; }
    CredentialStore CredentialStore { get; }
    Control OwnerControl { get; }
    OperationGuard OperationGuard { get; }
    bool IsRefreshing { get; }
    bool RenameInProgress { set; }
    DialogResult ShowModal(Form dialog);
    void SaveAndRefresh(Guid? selectCredentialId = null, int fallbackIndex = -1);
    void UpdateStatus(string text);
    void UpdateButtonState();
    void RefreshGrid();
    void TriggerProcessRefresh(int delayMs = 0);
}
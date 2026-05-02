using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Provides read-only access to shared data for account panel handlers that only read state.
/// </summary>
public interface IAccountsPanelDataContext
{
    AppDatabase Database { get; }
    CredentialStore CredentialStore { get; }
    ProtectedBuffer PinDerivedKey { get; }
    bool IsRefreshing { get; }
}

/// <summary>
/// Provides access to shared operations and the owner control for account panel handlers that modify state.
/// </summary>
public interface IAccountsPanelOperationContext
{
    Control OwnerControl { get; }
    OperationGuard OperationGuard { get; }
    bool RenameInProgress { set; }
    DialogResult ShowModal(Form dialog);
    void SaveAndRefresh(Guid? selectCredentialId = null, int fallbackIndex = -1);
    void UpdateStatus(string text);
    void UpdateButtonState();
    void SetControlsEnabled(bool enabled);
    void SaveLastPrefsPath(string path);
    void RefreshGrid();
    void TriggerProcessRefresh(int delayMs = 0);
}

/// <summary>
/// Combined context interface for handlers that need both data and operations.
/// Implemented by <see cref="Forms.AccountsPanel"/>.
/// </summary>
public interface IAccountsPanelContext : IAccountsPanelDataContext, IAccountsPanelOperationContext
{
}

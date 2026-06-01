using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public interface IAccountsPanelSelectionSaveView
{
    AppDatabase Database { get; }
    CredentialStore CredentialStore { get; }
    SessionContext Session { get; }
    Task RefreshGridAsync(CancellationToken cancellationToken = default);
    void SelectBySid(string sid);
    void RaiseDataChanged();
}

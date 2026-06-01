using RunFence.Core;

namespace RunFence.Account.UI;

public class AccountsPanelSelectionSaveCoordinator(SessionPersistenceHelper persistenceHelper)
{
    private IAccountsPanelSelectionSaveView? _view;

    public void Initialize(IAccountsPanelSelectionSaveView view)
        => _view = view;

    public async Task SaveRefreshAndSelectAsync(string? sidToSelect, CancellationToken cancellationToken)
    {
        var view = GetView();
        cancellationToken.ThrowIfCancellationRequested();

        persistenceHelper.SaveCredentialStoreAndConfig(
            view.CredentialStore,
            view.Database,
            view.Session.PinDerivedKey);

        await view.RefreshGridAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(sidToSelect))
            view.SelectBySid(sidToSelect);
        view.RaiseDataChanged();
    }

    private IAccountsPanelSelectionSaveView GetView()
        => _view ?? throw new InvalidOperationException("AccountsPanelSelectionSaveCoordinator must be initialized before use.");
}

namespace RunFence.Account.UI.AppContainer;

public interface IAppContainerEditDialogNotifier
{
    void ShowValidationWarning(IWin32Window owner, string message);

    void ShowOperationError(IWin32Window owner, string message);

    void ShowRestartRequired(IWin32Window owner);

    void ShowComAccessWarning(IWin32Window owner, IReadOnlyList<string> warnings);

    void ShowPersistenceWarning(IWin32Window owner, string message);
}

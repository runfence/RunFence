using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Aggregates credential-related operations for AccountsPanel: password copy/type/rotate/set-empty
/// (via <see cref="AccountPasswordHandler"/>) and credential CRUD / account-edit flow
/// (via <see cref="AccountCredentialOperations"/>).
/// </summary>
public class AccountsPanelCredentialHandler : IDisposable
{
    private IAccountGridCallbacks _callbacks = null!;
    private Control _ownerControl = null!;
    private readonly ISessionProvider _sessionProvider;
    private readonly OperationGuard _operationGuard;
    private readonly IPreviousWindowTracker _windowTracker;
    private readonly AccountPasswordHandler _passwordHandler;
    private readonly AccountCredentialOperations _credentialOperations;

    public event Action<IReadOnlyList<InstallablePackage>, AccountRow>? InstallPackagesRequested;

    /// <summary>Raised when the credential operations need to open the create user dialog.</summary>
    public event Action<string?, string?>? CreateUserDialogRequested;

    /// <summary>Raised when a delete user action is triggered from the credential edit dialog.</summary>
    public event Action<AccountRow, int>? DeleteUserRequested;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    public AccountsPanelCredentialHandler(
        AccountCredentialOperations credentialOperations,
        AccountPasswordHandler passwordHandler,
        ISessionProvider sessionProvider,
        OperationGuard operationGuard,
        IPreviousWindowTracker windowTracker)
    {
        _credentialOperations = credentialOperations;
        _passwordHandler = passwordHandler;
        _sessionProvider = sessionProvider;
        _operationGuard = operationGuard;
        _windowTracker = windowTracker;

        _credentialOperations.InstallPackagesRequested += (packages, ar) =>
            InstallPackagesRequested?.Invoke(packages, ar);
        _credentialOperations.CreateUserDialogRequested += (username, password) =>
            CreateUserDialogRequested?.Invoke(username, password);
        _credentialOperations.DeleteUserRequested += (accountRow, selectedIndex) =>
            DeleteUserRequested?.Invoke(accountRow, selectedIndex);
        _credentialOperations.SaveAndRefreshRequested += (credId, fallbackIndex) =>
            SaveAndRefreshRequested?.Invoke(credId, fallbackIndex);
    }

    /// <summary>
    /// Binds the handler to the owner control and UI callbacks.
    /// Must be called from <see cref="Forms.AccountsPanel.BuildDynamicContent"/> before any operations.
    /// </summary>
    public void Initialize(Control ownerControl, IAccountGridCallbacks callbacks)
    {
        _ownerControl = ownerControl;
        _callbacks = callbacks;
        _credentialOperations.Initialize(ownerControl);
    }

    // --- Credential CRUD ---

    public void AddCredential(AccountRow? selectedRow)
        => _credentialOperations.AddCredential(selectedRow);

    public void EditCredential(AccountRow accountRow)
        => _credentialOperations.EditCredential(accountRow);

    public void EditAccount(AccountRow accountRow, int selectedIndex)
        => _credentialOperations.EditAccount(accountRow, selectedIndex);

    public void RemoveCredential(AccountRow accountRow, int selectedIndex)
        => _credentialOperations.RemoveCredential(accountRow, selectedIndex);

    // --- Password operations ---

    public void CopyPassword(AccountRow accountRow)
    {
        if (accountRow.Credential == null || accountRow.Credential.IsCurrentAccount || !accountRow.HasStoredPassword)
            return;

        var session = _sessionProvider.GetSession();
        _passwordHandler.CopyPassword(accountRow, session, session.CredentialStore, _operationGuard,
            _ownerControl.FindForm() ?? _ownerControl, text => _callbacks.UpdateStatus(text));
    }

    public void TypePassword(AccountRow accountRow)
    {
        if (accountRow.Credential == null || accountRow.Credential.IsCurrentAccount || !accountRow.HasStoredPassword)
            return;

        var session = _sessionProvider.GetSession();
        var previousHwnd = _windowTracker.PreviousWindow;
        _passwordHandler.TypePassword(accountRow, session, session.CredentialStore, _operationGuard,
            _ownerControl.FindForm() ?? _ownerControl, previousHwnd, text => _callbacks.UpdateStatus(text));
    }

    public void RotatePassword(AccountRow accountRow)
    {
        if (accountRow.Credential?.IsCurrentAccount == true || SidResolutionHelper.IsInteractiveUserSid(accountRow.Sid))
            return;

        var session = _sessionProvider.GetSession();
        _passwordHandler.RotatePassword(accountRow, session, session.CredentialStore, _operationGuard,
            _ownerControl.FindForm() ?? _ownerControl, text => _callbacks.UpdateStatus(text),
            id => SaveAndRefreshRequested?.Invoke(id, -1));
    }

    public void SetEmptyPassword(AccountRow accountRow)
    {
        var session = _sessionProvider.GetSession();
        _passwordHandler.SetEmptyPassword(accountRow, session, session.CredentialStore,
            _operationGuard, _ownerControl.FindForm() ?? _ownerControl, text => _callbacks.UpdateStatus(text),
            id => SaveAndRefreshRequested?.Invoke(id, -1));
    }

    public void Dispose()
    {
        _passwordHandler.Dispose();
        if (_windowTracker is IDisposable d)
            d.Dispose();
    }
}
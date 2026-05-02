using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Aggregates credential-related operations for AccountsPanel: password copy/type/rotate/set-empty
/// (via <see cref="AccountPasswordAccessHandler"/> and <see cref="AccountPasswordMutationHandler"/>),
/// credential CRUD (via <see cref="AccountCredentialCrudHandler"/>),
/// and the full edit-account flow (via <see cref="AccountEditOrchestrator"/>).
/// </summary>
public class AccountsPanelCredentialHandler : IDisposable
{
    private IAccountGridCallbacks _callbacks = null!;
    private Control _ownerControl = null!;
    private readonly OperationGuard _operationGuard;
    private readonly IPreviousWindowTracker _windowTracker;
    private readonly AccountPasswordAccessHandler _passwordAccessHandler;
    private readonly AccountPasswordMutationHandler _passwordMutationHandler;
    private readonly AccountCredentialCrudHandler _credentialCrudHandler;
    private readonly AccountEditOrchestrator _editOrchestrator;

    /// <summary>Raised when the credential operations need to open the create user dialog.</summary>
    public event Action<string?, ProtectedString?>? CreateUserDialogRequested;

    /// <summary>Raised when a delete user action is triggered from the credential edit dialog.</summary>
    public event Action<AccountRow, int>? DeleteUserRequested;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    public AccountsPanelCredentialHandler(
        AccountCredentialCrudHandler credentialCrudHandler,
        AccountEditOrchestrator editOrchestrator,
        AccountPasswordAccessHandler passwordAccessHandler,
        AccountPasswordMutationHandler passwordMutationHandler,
        OperationGuard operationGuard,
        IPreviousWindowTracker windowTracker,
        ToolLauncher launchService)
    {
        _credentialCrudHandler = credentialCrudHandler;
        _editOrchestrator = editOrchestrator;
        _passwordAccessHandler = passwordAccessHandler;
        _passwordMutationHandler = passwordMutationHandler;
        _operationGuard = operationGuard;
        _windowTracker = windowTracker;
        _editOrchestrator.InstallPackagesRequested += (packages, ar) =>
            launchService.InstallPackages(packages, new AccountLaunchIdentity(ar.Sid));
        _credentialCrudHandler.CreateUserDialogRequested += (username, password) =>
            CreateUserDialogRequested?.Invoke(username, password);
        _editOrchestrator.DeleteUserRequested += (accountRow, selectedIndex) =>
            DeleteUserRequested?.Invoke(accountRow, selectedIndex);
        _credentialCrudHandler.SaveAndRefreshRequested += (credId, fallbackIndex) =>
            SaveAndRefreshRequested?.Invoke(credId, fallbackIndex);
        _editOrchestrator.SaveAndRefreshRequested += (credId, fallbackIndex) =>
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
        _editOrchestrator.Initialize(ownerControl);
        _passwordAccessHandler.Initialize(_operationGuard, ownerControl, text => _callbacks.UpdateStatus(text));
        _passwordMutationHandler.Initialize(_operationGuard, ownerControl, text => _callbacks.UpdateStatus(text));
    }

    // --- Credential CRUD ---

    public void AddCredential(AccountRow? selectedRow)
        => _credentialCrudHandler.AddCredential(selectedRow);

    public void EditCredential(AccountRow accountRow)
        => _credentialCrudHandler.EditCredential(accountRow);

    public void EditAccount(AccountRow accountRow, int selectedIndex)
        => _ = _editOrchestrator.EditAccount(accountRow, selectedIndex);

    public void RemoveCredential(AccountRow accountRow, int selectedIndex)
        => _credentialCrudHandler.RemoveCredential(accountRow, selectedIndex);

    // --- Password operations ---

    public async Task CopyPasswordAsync(AccountRow accountRow)
    {
        if (accountRow.Credential == null || accountRow.Credential.IsCurrentAccount || !accountRow.HasStoredPassword)
            return;

        await _passwordAccessHandler.CopyPasswordAsync(accountRow);
    }

    public async Task TypePasswordAsync(AccountRow accountRow)
    {
        if (accountRow.Credential == null || accountRow.Credential.IsCurrentAccount || !accountRow.HasStoredPassword)
            return;

        await _passwordAccessHandler.TypePasswordAsync(accountRow, _windowTracker.PreviousWindow);
    }

    public void RotatePassword(AccountRow accountRow)
        => _passwordMutationHandler.RotatePassword(accountRow, id => SaveAndRefreshRequested?.Invoke(id, -1));

    public void SetEmptyPassword(AccountRow accountRow)
        => _passwordMutationHandler.SetEmptyPassword(accountRow, id => SaveAndRefreshRequested?.Invoke(id, -1));

    public void Dispose()
    {
        _passwordAccessHandler.Dispose();
        if (_windowTracker is IDisposable d)
            d.Dispose();
    }
}

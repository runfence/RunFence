using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public interface IAccountCreationDialog : IDisposable
{
    event Func<Task<bool>>? CreateConfirmRequested;

    string? NewUsername { get; }
    bool IsEphemeral { get; }
    string? SettingsImportPath { get; }
    PrivilegeLevel SelectedPrivilegeLevel { get; }
    bool AllowInternet { get; }
    bool AllowLocalhost { get; }
    bool AllowLan { get; }
    bool FirewallSettingsChanged { get; }
    List<string> Errors { get; }
    string? CreatedSid { get; }
    ProtectedString? CreatedPassword { get; }
    CreateAccountStatus CreatedAccountStatus { get; }
    string? CreatedAccountErrorMessage { get; }
    bool UsersGroupUnchecked { get; }
    bool AdminGroupChecked { get; }
    IReadOnlyList<InstallablePackage> SelectedInstallPackages { get; }
    CreatedAccountRollbackState? CreatedRollbackState { get; }

    void InitializeForCreate(
        string? prefillUsername = null,
        ProtectedString? prefillPassword = null,
        int currentHiddenCount = 0);

    Task<DialogResult> ShowCreateDialogAsync(IWin32Window owner);
}

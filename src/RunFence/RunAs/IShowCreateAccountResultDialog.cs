using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Account.UI.Forms;

namespace RunFence.RunAs;

public interface IShowCreateAccountResultDialog : IDisposable, IRunAsAccountCreationRollbackStateProvider
{
    string? CreatedSid { get; }
    ProtectedString? CreatedPassword { get; }
    string? NewUsername { get; }
    bool IsEphemeral { get; }
    PrivilegeLevel SelectedPrivilegeLevel { get; }
    bool FirewallSettingsChanged { get; }
    bool AllowInternet { get; }
    bool AllowLocalhost { get; }
    bool AllowLan { get; }
    string? SettingsImportPath { get; }
    List<string> Errors { get; }
    CreateAccountStatus CreatedAccountStatus { get; }
}

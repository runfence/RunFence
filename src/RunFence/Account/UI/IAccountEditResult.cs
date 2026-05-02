using RunFence.Core;

namespace RunFence.Account.UI;

public interface IAccountEditResult
{
    ProtectedString? NewPassword { get; }
    string? SettingsImportPath { get; }
    List<string> Errors { get; }
}

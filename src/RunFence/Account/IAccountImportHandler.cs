using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

public record ImportAccount(CredentialEntry CredEntry, string Username);

public interface IAccountImportHandler
{
    Task<string?> RunImportAsync(
        List<ImportAccount> accounts,
        CredentialStore credStore,
        ProtectedBuffer pinKey,
        Func<string?> selectFile,
        Action<string> appendLog,
        Action enableOk,
        Action<string> onStatusUpdate,
        AppDatabase? db = null);
}

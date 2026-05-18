using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

public interface IAccountConfigMigrationService
{
    bool TargetHasExistingData(string targetSid);
    void MigrateToAccount(CredentialStore store, string targetSid,
        ProtectedString targetPassword, ISecureSecretSnapshotSource currentPinKey);
    void DeleteCurrentAccountData();
}

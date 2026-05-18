using System.Text;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;

namespace RunFence.Account;

public class AccountConfigMigrationService(
    IProfilePathResolver profilePathResolver,
    ICredentialEncryptionSpanService encryptionService,
    IUserImpersonationHelper impersonationHelper,
    IConfigPaths configPaths,
    IManagedPersistenceFileCleaner managedPersistenceFileCleaner,
    ILoggingService log)
    : IAccountConfigMigrationService
{
    public bool TargetHasExistingData(string targetSid)
    {
        var profilePath = profilePathResolver.TryGetProfilePath(targetSid);
        if (profilePath == null)
            return false;

        var targetCredPath = Path.Combine(profilePath, @"AppData\Local\RunFence\credentials.dat");
        var targetConfigPath = Path.Combine(profilePath, @"AppData\Roaming\RunFence\config.dat");
        return File.Exists(targetCredPath) || File.Exists(targetConfigPath);
    }

    public void MigrateToAccount(CredentialStore store, string targetSid,
        ProtectedString targetPassword, ISecureSecretSnapshotSource currentPinKey)
    {
        var reencryptEntries = new List<(Guid id, string sid, ProtectedString decrypted)>();
        var copyAsIsEntries = new List<CredentialEntry>();

        foreach (var cred in store.Credentials)
        {
            if (cred.IsCurrentAccount)
            {
                if (cred.EncryptedPassword.Length > 0)
                {
                    reencryptEntries.Add((
                        cred.Id,
                        cred.Sid,
                        currentPinKey.TransformSnapshot(key => encryptionService.Decrypt(cred.EncryptedPassword, key))));
                }
                continue;
            }
            if (cred.EncryptedPassword.Length == 0)
            {
                copyAsIsEntries.Add(CredentialStoreCloneHelper.CloneEntry(cred));
                continue;
            }

            reencryptEntries.Add((
                cred.Id,
                cred.Sid,
                currentPinKey.TransformSnapshot(key => encryptionService.Decrypt(cred.EncryptedPassword, key))));
        }

        try
        {
            var (profilePath, newStore) = impersonationHelper.RunImpersonated(
                targetSid, targetPassword, () =>
                {
                    var result = new CredentialStore
                    {
                        ArgonSalt = store.ArgonSalt.ToArray(),
                        EncryptedCanary = store.EncryptedCanary.ToArray()
                    };
                    foreach (var entry in copyAsIsEntries)
                        result.Credentials.Add(entry);
                    foreach (var (id, sid, password) in reencryptEntries)
                    {
                        var encryptedPassword = currentPinKey.TransformSnapshot(key => encryptionService.Encrypt(password, key));
                        result.Credentials.Add(new CredentialEntry
                        {
                            Id = id,
                            Sid = sid,
                            EncryptedPassword = encryptedPassword
                        });
                    }

                    return result;
                });

            var targetCredPath = Path.Combine(profilePath, @"AppData\Local\RunFence\credentials.dat");
            var targetConfigPath = Path.Combine(profilePath, @"AppData\Roaming\RunFence\config.dat");
            var targetLicensePath = Path.Combine(profilePath, @"AppData\Roaming\RunFence\license.dat");

            Directory.CreateDirectory(Path.GetDirectoryName(targetCredPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetConfigPath)!);
            var credJson = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(newStore, JsonDefaults.Options));
            File.WriteAllBytes(targetCredPath, credJson);
            File.Copy(configPaths.ConfigFilePath, targetConfigPath, overwrite: true);
            if (File.Exists(configPaths.LicenseFilePath))
                File.Copy(configPaths.LicenseFilePath, targetLicensePath, overwrite: true);
            log.Info($"Migration to {targetSid} complete.");
        }
        finally
        {
            foreach (var (_, _, pw) in reencryptEntries)
                pw.Dispose();
        }
    }

    public void DeleteCurrentAccountData()
    {
        managedPersistenceFileCleaner.DeletePrimaryAndManagedArtifacts(configPaths.CredentialsFilePath);
        managedPersistenceFileCleaner.DeletePrimaryAndManagedArtifacts(configPaths.ConfigFilePath);
        managedPersistenceFileCleaner.DeletePrimaryAndManagedArtifacts(configPaths.LicenseFilePath);
        managedPersistenceFileCleaner.DeletePrimaryAndManagedArtifacts(configPaths.RememberPinFilePath);
    }
}

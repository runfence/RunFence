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
    ICredentialEncryptionService encryptionService,
    IUserImpersonationHelper impersonationHelper,
    IConfigPaths configPaths,
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
        ProtectedString targetPassword, byte[] currentPinKey)
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
                        encryptionService.Decrypt(cred.EncryptedPassword, currentPinKey)));
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
                encryptionService.Decrypt(cred.EncryptedPassword, currentPinKey)));
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
                        result.Credentials.Add(new CredentialEntry
                        {
                            Id = id,
                            Sid = sid,
                            EncryptedPassword = encryptionService.Encrypt(password, currentPinKey)
                        });
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
            if (File.Exists(PathConstants.LicenseFilePath))
                File.Copy(PathConstants.LicenseFilePath, targetLicensePath, overwrite: true);
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
        TryDelete(configPaths.CredentialsFilePath);
        TryDelete(configPaths.ConfigFilePath);
        TryDelete(PathConstants.LicenseFilePath);
        TryDelete(configPaths.RememberPinFilePath);
    }

    private void TryDelete(string path)
    {
        if (!File.Exists(path))
            return;
        File.Delete(path);
        log.Info($"Deleted {path}.");
    }
}

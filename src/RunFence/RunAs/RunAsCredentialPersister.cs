using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs.UI;
using RunFence.Security;

namespace RunFence.RunAs;

/// <summary>
/// Handles persistence of RunAs credential choices: last-used account/container
/// and ad-hoc "remember password" saves.
/// </summary>
public class RunAsCredentialPersister(
    IAppStateProvider appState,
    SessionContext session,
    ICredentialEncryptionSpanService encryptionService,
    IMainConfigPersistence mainConfigPersistence,
    ICredentialStorePersistence credentialStorePersistence,
    ILoggingService log)
{
    public string? LastUsedRunAsAccountSid { get; private set; } = appState.Database.Settings.LastUsedRunAsAccountSid;

    public string? LastUsedRunAsContainerName { get; private set; } = appState.Database.Settings.LastUsedRunAsContainerName;

    public void SetLastUsedAccount(string? accountSid)
    {
        LastUsedRunAsAccountSid = accountSid;
        LastUsedRunAsContainerName = null;
        PersistLastUsedSelection();
    }

    public void SetLastUsedContainer(string? containerName)
    {
        LastUsedRunAsContainerName = containerName;
        LastUsedRunAsAccountSid = null;
        PersistLastUsedSelection();
    }

    private void PersistLastUsedSelection()
    {
        var settings = appState.Database.Settings;
        if (settings.LastUsedRunAsAccountSid == LastUsedRunAsAccountSid &&
            settings.LastUsedRunAsContainerName == LastUsedRunAsContainerName)
            return;
        settings.LastUsedRunAsAccountSid = LastUsedRunAsAccountSid;
        settings.LastUsedRunAsContainerName = LastUsedRunAsContainerName;
        try
        {
            mainConfigPersistence.SaveConfig(
                appState.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to persist last used RunAs selection: {ex.Message}");
        }
    }

    /// <summary>
    /// If the user requested "remember password" for an ad-hoc credential, encrypts and stores it.
    /// </summary>
    public void TrySaveRememberedPassword(RunAsDialogResult result)
    {
        if (!result.RememberPassword || result.AdHocPassword == null || result.Credential == null)
            return;
        try
        {
            var encryptedPassword = session.PinDerivedKey.TransformSnapshot(key =>
                encryptionService.Encrypt(result.AdHocPassword, key));

            var existing = session.CredentialStore.Credentials
                .FirstOrDefault(c => string.Equals(c.Sid, result.Credential.Sid, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.EncryptedPassword = encryptedPassword;
            else
                session.CredentialStore.Credentials.Add(new CredentialEntry
                {
                    Id = Guid.NewGuid(),
                    Sid = result.Credential.Sid,
                    EncryptedPassword = encryptedPassword
                });
            credentialStorePersistence.SaveCredentialStore(session.CredentialStore);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remember password: {ex.Message}");
        }
    }
}

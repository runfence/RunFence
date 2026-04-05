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
    ICredentialEncryptionService encryptionService,
    IDatabaseService databaseService,
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
            using var scope = session.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
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
            byte[] encryptedPassword;
            using (var scope = session.PinDerivedKey.Unprotect())
                encryptedPassword = encryptionService.Encrypt(result.AdHocPassword, scope.Data);

            session.CredentialStore.Credentials.Add(new CredentialEntry
            {
                Id = Guid.NewGuid(),
                Sid = result.Credential.Sid,
                EncryptedPassword = encryptedPassword
            });
            databaseService.SaveCredentialStore(session.CredentialStore);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remember password: {ex.Message}");
        }
    }
}
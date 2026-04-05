using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup;

/// <summary>
/// Startup initialization helpers extracted from Program to enable reuse in LockManager (PIN reset flow)
/// and testability via AppInitializationHelperTests.
/// </summary>
public class AppInitializationHelper(ISidResolver sidResolver) : IAppInitializationHelper
{
    public bool EnsureCurrentAccountCredential(CredentialStore credentialStore, AppDatabase? database = null)
    {
        var currentSid = sidResolver.GetCurrentUserSid();

        if (database != null)
        {
            var resolvedName = sidResolver.TryResolveName(currentSid) ?? Environment.UserName;
            database.UpdateSidName(currentSid, resolvedName);
        }

        if (credentialStore.Credentials.Any(c => c.IsCurrentAccount))
            return false;

        credentialStore.Credentials.Insert(0, new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = currentSid,
            EncryptedPassword = Array.Empty<byte>()
        });

        return true;
    }

    public void EnsureInteractiveUserSidName(AppDatabase database)
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid == null)
            return;

        var resolvedName = sidResolver.TryResolveName(interactiveSid) ?? interactiveSid;
        database.UpdateSidName(interactiveSid, resolvedName);
    }

    public bool NormalizeAccountSids(IList<AppEntry> apps, string currentAccountSid)
    {
        bool changed = false;
        foreach (var app in apps)
        {
            // AppContainer entries have empty AccountSid by design — never overwrite them
            if (string.IsNullOrEmpty(app.AccountSid) && app.AppContainerName == null)
            {
                app.AccountSid = currentAccountSid;
                changed = true;
            }
        }

        return changed;
    }

    public void PopulateDefaultIpcCallers(AppDatabase database)
    {
        var currentSid = sidResolver.GetCurrentUserSid();
        database.GetOrCreateAccount(currentSid).IsIpcCaller = true;
        database.UpdateSidName(currentSid,
            sidResolver.TryResolveName(currentSid) ?? currentSid);

        var interactiveUserSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
        if (interactiveUserSid != null &&
            !string.Equals(interactiveUserSid, currentSid, StringComparison.OrdinalIgnoreCase))
        {
            database.GetOrCreateAccount(interactiveUserSid).IsIpcCaller = true;
            database.UpdateSidName(interactiveUserSid,
                sidResolver.TryResolveName(interactiveUserSid) ?? interactiveUserSid);
        }
    }
}
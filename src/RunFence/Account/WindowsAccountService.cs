using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account;

public class WindowsAccountService(
    ILoggingService log,
    IAccountValidationService accountValidation,
    IAccountLoginRestrictionService restrictions,
    ILocalUserProvider localUserProvider,
    ILocalSamSidResolver localSamSidResolver,
    ILocalAccountProvisioningService localAccountProvisioning,
    IFolderHandlerService folderHandlerService)
    : IWindowsAccountService
{
    public void DeleteSamAccount(string sid)
    {
        accountValidation.ValidateNotCurrentAccount(sid, "delete");
        accountValidation.ValidateNotLastAdmin(sid, "delete");
        accountValidation.ValidateNotInteractiveUser(sid, "delete");

        try
        {
            localAccountProvisioning.DeleteLocalUserBySid(sid);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to delete user account {sid}", ex);
            throw new InvalidOperationException($"Failed to delete account: {ex.Message}", ex);
        }
    }

    public string CreateLocalUser(string username, ProtectedString password)
    {
        var accountCreated = false;
        try
        {
            localAccountProvisioning.CreateLocalUser(username, password);
            accountCreated = true;
            localUserProvider.InvalidateCache();
            var sid = localSamSidResolver.GetRequiredLocalUserSid(username);

            try
            {
                int setInfoResult = localAccountProvisioning.SetDisplayName(username, username);
                if (setInfoResult != 0)
                    log.Warn($"NetUserSetInfo(1011) for display name of '{username}' failed with code {setInfoResult}. Non-critical.");
            }
            catch (Exception ex)
            {
                log.Warn($"NetUserSetInfo(1011) for display name of '{username}' failed: {ex.Message}. Non-critical.");
            }

            log.Info($"Created local user: {username} ({sid})");
            return sid;
        }
        catch (InvalidOperationException ex) when (accountCreated)
        {
            DeleteCreatedAccountAfterFailure(username, ex);
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (accountCreated)
        {
            DeleteCreatedAccountAfterFailure(username, ex);
            log.Error($"Failed to create local user {username}", ex);
            throw new InvalidOperationException($"Failed to create account: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to create local user {username}", ex);
            throw new InvalidOperationException($"Failed to create account: {ex.Message}", ex);
        }
    }

    public void RenameAccount(string sid, string currentUsername, string newUsername)
    {
        try
        {
            var wasHidden = restrictions.IsAccountHidden(currentUsername);
            int result = localAccountProvisioning.RenameLocalUser(currentUsername, newUsername);
            if (result != 0)
            {
                var msg = result switch
                {
                    2220 => $"Account '{currentUsername}' not found.",
                    2224 => $"Account name '{newUsername}' is already in use.",
                    _ => $"NetUserSetInfo failed with error code {result}."
                };
                throw new InvalidOperationException(msg);
            }

            if (wasHidden)
            {
                restrictions.SetAccountHidden(newUsername, sid, true);
                try
                {
                    restrictions.SetAccountHidden(currentUsername, sid, false);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to remove old hidden registry entry for '{currentUsername}' after rename: {ex.Message}");
                }
            }

            if (folderHandlerService.IsRegistered(sid))
            {
                try
                {
                    folderHandlerService.Unregister(sid);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to unregister folder handler for '{currentUsername}' ({sid}) after rename: {ex.Message}");
                }
            }

            localUserProvider.InvalidateCache();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to rename account {currentUsername} ({sid}) to {newUsername}", ex);
            throw new InvalidOperationException($"Failed to rename account: {ex.Message}", ex);
        }
    }

    private void DeleteCreatedAccountAfterFailure(string username, Exception originalFailure)
    {
        try
        {
            localAccountProvisioning.DeleteLocalUserByName(username);
            localUserProvider.InvalidateCache();
            log.Warn($"Deleted newly created local user '{username}' after account creation failed: {originalFailure.Message}");
        }
        catch (Exception cleanupEx)
        {
            log.Error($"Failed to delete newly created local user '{username}' after account creation failed", cleanupEx);
        }
    }
}

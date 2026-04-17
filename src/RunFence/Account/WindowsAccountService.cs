using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account;

public class WindowsAccountService(
    ILoggingService log,
    IAccountValidationService accountValidation,
    IAccountLoginRestrictionService restrictions,
    ISidResolver sidResolver,
    ILocalUserProvider localUserProvider,
    IFolderHandlerService folderHandlerService)
    : IWindowsAccountService
{
    // --- Delete User ---

    /// <remarks>
    /// Dual deletion intentional: UserPrincipal.Delete() sometimes leaves the profile directory.
    /// WMI Win32_UserProfile.Delete() is the fallback to ensure complete cleanup.
    /// </remarks>
    public void DeleteUser(string sid)
    {
        accountValidation.ValidateNotCurrentAccount(sid, "delete");
        accountValidation.ValidateNotLastAdmin(sid, "delete");
        accountValidation.ValidateNotInteractiveUser(sid, "delete");

        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            user?.Delete();

            try
            {
                if (!WindowsAccountNative.DeleteProfile(sid, null, IntPtr.Zero))
                    log.Warn($"DeleteProfile for {sid} returned false. Win32 error: {Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                log.Warn($"DeleteProfile for {sid} failed: {ex.Message}");
            }

            // Validate SID format before embedding in WMI query to prevent injection
            _ = new SecurityIdentifier(sid);

            string query = $"SELECT * FROM Win32_UserProfile WHERE SID = '{sid}'";
            using (var searcher = new ManagementObjectSearcher(query))
            {
                foreach (ManagementObject profile in searcher.Get())
                    profile.Delete();
            }

            log.Info($"Deleted Windows user account: {sid}");
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

    // --- Create User ---

    public string CreateLocalUser(string username, string password)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = new UserPrincipal(context);
            user.Name = username;
            user.DisplayName = username;
            user.PasswordNeverExpires = true;
            user.Enabled = true;
            user.SetPassword(password);
            user.Save();

            var sid = user.Sid.Value;
            log.Info($"Created local user: {username} ({sid})");
            localUserProvider.InvalidateCache();
            return sid;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to create local user {username}", ex);
            throw new InvalidOperationException($"Failed to create account: {ex.Message}", ex);
        }
    }

    // --- Profile Path ---

    public string? GetProfilePath(string sid) => sidResolver.TryGetProfilePath(sid);

    // --- Rename ---

    public void RenameAccount(string sid, string currentUsername, string newUsername)
    {
        try
        {
            var wasHidden = restrictions.IsAccountHidden(currentUsername);

            var info = new WindowsAccountNative.USER_INFO_0 { usri0_name = newUsername };
            int result = WindowsAccountNative.NetUserSetInfo(null, currentUsername, 0, ref info, out _);
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

            log.Info($"Renamed account '{currentUsername}' ({sid}) to '{newUsername}'");
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

            // If the account had a folder handler registered, unregister it now so that any
            // stale entries referencing the old account state are removed. The handler will be
            // re-registered by the next launch if needed.
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

    // --- Password Validation ---

    private const int Logon32LogonNetwork = 3;
    private const int Logon32LogonBatch = 4;
    private const int Logon32LogonInteractive = 2;
    private const int Logon32ProviderDefault = 0;

    public string? ValidatePassword(string sid, string password, string usernameFallback)
    {
        var (domain, username) = SidNameResolver.ResolveDomainAndUsernameWithFallback(sid, usernameFallback, sidResolver);
        var domainArg = string.IsNullOrEmpty(domain) ? null : domain;

        // Try logon types in order; if type-not-granted for all, skip validation
        int[] logonTypes = [Logon32LogonNetwork, Logon32LogonBatch, Logon32LogonInteractive];
        foreach (var logonType in logonTypes)
        {
            if (WindowsAccountNative.LogonUser(username, domainArg, password, logonType, Logon32ProviderDefault, out var token))
            {
                ProcessNative.CloseHandle(token);
                return null; // Success
            }

            var error = Marshal.GetLastWin32Error();
            if (error == ProcessLaunchNative.Win32ErrorLogonFailure)
                return "Invalid username or password.";
            if (error != ProcessLaunchNative.Win32ErrorLogonTypeNotGranted)
                return $"Credential validation failed: {new Win32Exception(error).Message}";
            // ERROR_LOGON_TYPE_NOT_GRANTED — try next type
        }

        // All types returned type-not-granted — can't verify, allow through
        return null;
    }
}

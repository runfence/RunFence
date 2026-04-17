using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Account;

public class AccountLoginRestrictionService(
    IGroupPolicyScriptHelper gpHelper,
    ILoggingService log,
    IAccountValidationService accountValidation) : IAccountLoginRestrictionService
{
    public bool IsAccountHidden(string username)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList");
            var value = key?.GetValue(username);
            if (value is int intValue)
                return intValue == 0;
            return false;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check hidden status for {username}", ex);
            return false;
        }
    }

    public void SetAccountHidden(string username, string sid, bool hidden)
    {
        if (hidden)
        {
            accountValidation.ValidateNotInteractiveUser(sid, "hide");
            accountValidation.ValidateNotLastAdmin(sid, "hide");
        }

        try
        {
            if (hidden)
            {
                using var key = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList");
                key.SetValue(username, 0, RegistryValueKind.DWord);
                log.Info($"Set account hidden: {username}");
            }
            else
            {
                // Read-only check first — if the value doesn't exist, it's already not hidden (no-op).
                // This avoids requiring write access to HKLM when there is nothing to delete.
                using var readKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList");
                if (readKey?.GetValue(username) == null)
                    return;

                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList", true);
                if (key != null)
                {
                    key.DeleteValue(username, false);
                    log.Info($"Set account visible: {username}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set hidden status for {username}", ex);
            throw;
        }
    }

    public bool IsLoginBlockedBySid(string sid)
    {
        try
        {
            return gpHelper.IsLoginBlocked(sid);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check login-blocked status for {sid}", ex);
            return false;
        }
    }

    public bool? GetNoLogonState(string sid, string? username)
    {
        try
        {
            var isScriptBlocked = gpHelper.IsLoginBlocked(sid);
            var isHidden = username != null && IsAccountHidden(username);
            if (isScriptBlocked && isHidden)
                return true;
            if (!isScriptBlocked && !isHidden)
                return false;
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check no-logon state for {sid}", ex);
            return false;
        }
    }

    public void SetUacAdminEnumeration(bool enumerate)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\CredUI";
        if (enumerate)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue("EnumerateAdministrators", throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.LocalMachine.CreateSubKey(keyPath);
            key.SetValue("EnumerateAdministrators", 0, RegistryValueKind.DWord);
        }
    }

    public int GetCurrentUacAdminEnumeration()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\CredUI");
            if (key?.GetValue("EnumerateAdministrators") is int v)
                return v;
        }
        catch
        {
        }

        return -1; // absent — sentinel meaning "delete value on restore"
    }

    public SetLoginBlockedResult SetLoginBlockedBySid(string sid, string username, bool blocked)
    {
        if (blocked)
        {
            accountValidation.ValidateNotInteractiveUser(sid, "block login for");
            accountValidation.ValidateNotLastAdmin(sid, "block login for");
        }

        SetLoginBlockedResult result;
        try
        {
            result = gpHelper.SetLoginBlocked(sid, blocked);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set login-blocked status for {username}", ex);
            throw;
        }

        try
        {
            SetAccountHidden(username, sid, blocked);
        }
        catch (Exception ex)
        {
            log.Error($"SetAccountHidden failed for {username}; rolling back login-blocked change", ex);
            try
            {
                gpHelper.SetLoginBlocked(sid, !blocked);
            }
            catch
            {
                /* rollback best-effort */
            }

            throw;
        }

        log.Info(blocked ? $"Login blocked for: {username}" : $"Login unblocked for: {username}");
        return result;
    }
}
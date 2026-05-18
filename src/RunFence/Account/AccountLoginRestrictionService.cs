using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Account;

public class AccountLoginRestrictionService(
    IGroupPolicyScriptHelper gpHelper,
    ILoggingService log,
    IAccountValidationService accountValidation) : IAccountLoginRestrictionService
{
    private const string UserListKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList";

    public bool IsAccountHidden(string username)
    {
        try
        {
            return GetAccountHiddenStateOrThrow(username);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check hidden status for {username}", ex);
            return false;
        }
    }

    public bool GetAccountHiddenStateOrThrow(string username)
    {
        using var key = Registry.LocalMachine.OpenSubKey(UserListKeyPath);
        var value = key?.GetValue(username);
        return value is int intValue && intValue == 0;
    }

    public void SetAccountHidden(string username, string sid, bool hidden)
    {
        if (hidden)
        {
            accountValidation.ValidateNotInteractiveUser(sid, "hide");
            accountValidation.ValidateNotLastAdmin(sid, "hide");
        }

        SetAccountHiddenCore(username, hidden);
    }

    public void RestoreAccountHiddenState(string username, bool hidden)
    {
        SetAccountHiddenCore(username, hidden);
    }

    private void SetAccountHiddenCore(string username, bool hidden)
    {
        try
        {
            if (hidden)
            {
                using var key = Registry.LocalMachine.CreateSubKey(UserListKeyPath);
                key.SetValue(username, 0, RegistryValueKind.DWord);
                log.Info($"Set account hidden: {username}");
            }
            else
            {
                // Read-only check first — if the value doesn't exist, it's already not hidden (no-op).
                // This avoids requiring write access to HKLM when there is nothing to delete.
                using var readKey = Registry.LocalMachine.OpenSubKey(UserListKeyPath);
                if (readKey?.GetValue(username) == null)
                    return;

                using var key = Registry.LocalMachine.OpenSubKey(UserListKeyPath, true);
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

        var previousScriptBlocked = gpHelper.IsLoginBlocked(sid);
        var previousHiddenState = GetAccountHiddenStateOrThrow(username);

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
                gpHelper.SetLoginBlocked(sid, previousScriptBlocked);
                RestoreAccountHiddenState(username, previousHiddenState);
            }
            catch (Exception rollbackEx)
            {
                log.Error($"Rollback failed for login-blocked change on {username}", rollbackEx);
                throw new AccountRestrictionOperationException(
                    $"{ex.Message} Rollback failed: {rollbackEx.Message}",
                    AccountRestrictionStatus.Failed,
                    rollbackAttempted: true,
                    ex);
            }

            throw new AccountRestrictionOperationException(
                ex.Message,
                AccountRestrictionStatus.RolledBack,
                rollbackAttempted: true,
                ex);
        }

        log.Info(blocked ? $"Login blocked for: {username}" : $"Login unblocked for: {username}");
        return result;
    }
}

using Microsoft.Win32;

namespace RunFence.SecurityScanner;

public class AccountPolicyDataAccess(ISamAccountPolicyNativeReader samReader) : IAccountPolicyDataAccess
{
    public int? GetAccountLockoutThreshold() => samReader.GetAccountLockoutThreshold();

    public bool? GetAdminAccountLockoutEnabled() => samReader.GetAdminAccountLockoutEnabled();

    public bool? GetBlankPasswordRestrictionEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
            if (key?.GetValue("LimitBlankPasswordUse") is int value)
                return value != 0;
            return null;
        }
        catch
        {
            return null;
        }
    }
}

using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Security;
using RunFence.Core;

namespace RunFence.Account;

public class AccountPasswordService(ILoggingService log) : IAccountPasswordService
{
    public void ChangeAccountPassword(string sid, SecureString oldPassword, string newPassword)
    {
        IntPtr oldPtr = IntPtr.Zero;
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            if (user == null)
                throw new InvalidOperationException($"Account not found for SID {sid}.");

            // Marshal old password to unmanaged memory and pass the pointer directly to
            // NetUserChangePassword — no managed string is created for the old password.
            oldPtr = Marshal.SecureStringToGlobalAllocUnicode(oldPassword);
            int result = AccountPasswordNative.NetUserChangePassword(Environment.MachineName, user.SamAccountName, oldPtr, newPassword);
            if (result != 0)
                throw new Win32Exception(result);

            log.Info($"Changed password for account with SID {sid}");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Win32Exception)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to change password for SID {sid}", ex);
            throw new InvalidOperationException($"Failed to change password: {ex.Message}", ex);
        }
        finally
        {
            if (oldPtr != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(oldPtr);
        }
    }

    public void AdminResetAccountPassword(string sid, string newPassword)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            if (user == null)
                throw new InvalidOperationException($"Account not found for SID {sid}.");

            user.SetPassword(newPassword);
            user.Save();
            log.Info($"Admin-reset password for account with SID {sid}");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to admin-reset password for SID {sid}", ex);
            throw new InvalidOperationException($"Failed to reset password: {ex.Message}", ex);
        }
    }
}
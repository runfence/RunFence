using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Account;

public class AccountPasswordService(ILoggingService log) : IAccountPasswordService
{
    public void ChangeAccountPassword(string sid, ProtectedString oldPassword, ProtectedString newPassword)
    {
        IntPtr oldPtr = IntPtr.Zero;
        IntPtr newPtr = IntPtr.Zero;
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            if (user == null)
                throw new InvalidOperationException($"Account not found for SID {sid}.");

            oldPtr = oldPassword.AllocUnicode();
            newPtr = newPassword.AllocUnicode();
            int result = AccountPasswordNative.NetUserChangePassword(Environment.MachineName, user.SamAccountName, oldPtr, newPtr);
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
            if (newPtr != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(newPtr);
        }
    }

    public void AdminResetAccountPassword(string sid, ProtectedString newPassword)
    {
        IntPtr passwordPtr = IntPtr.Zero;
        IntPtr structPtr = IntPtr.Zero;
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            if (user == null)
                throw new InvalidOperationException($"Account not found for SID {sid}.");

            passwordPtr = newPassword.AllocUnicode();
            structPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(structPtr, passwordPtr);

            int result = WindowsAccountNative.NetUserSetInfo(null, user.SamAccountName, 1003, structPtr, out _);
            if (result != 0)
                throw new Win32Exception(result);

            log.Info($"Admin-reset password for account with SID {sid}");
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
            log.Error($"Failed to admin-reset password for SID {sid}", ex);
            throw new InvalidOperationException($"Failed to reset password: {ex.Message}", ex);
        }
        finally
        {
            if (passwordPtr != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
            if (structPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(structPtr);
        }
    }
}
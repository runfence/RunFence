using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account;

public class AccountPasswordService(ILoggingService log, ISidResolver sidResolver) : IAccountPasswordService
{
    private const int Logon32LogonNetwork = 3;
    private const int Logon32LogonBatch = 4;
    private const int Logon32LogonInteractive = 2;
    private const int Logon32ProviderDefault = 0;

    public AccountPasswordResult ValidatePassword(string sid, ProtectedString password, string usernameFallback)
    {
        var (domain, username) = SidNameResolver.ResolveDomainAndUsernameWithFallback(sid, usernameFallback, sidResolver);
        var domainArg = string.IsNullOrEmpty(domain) ? null : domain;

        return password.UseUnicodeSnapshot<AccountPasswordResult>(snapshot =>
        {
            int[] logonTypes = [Logon32LogonNetwork, Logon32LogonBatch, Logon32LogonInteractive];
            foreach (var logonType in logonTypes)
            {
                if (WindowsAccountNative.LogonUser(username, domainArg, snapshot.DangerousGetIntPtr(), logonType, Logon32ProviderDefault, out var token))
                {
                    ProcessNative.CloseHandle(token);
                    return new(AccountPasswordStatus.Succeeded, sid, null);
                }

                var error = Marshal.GetLastWin32Error();
                if (error == ProcessLaunchNative.Win32ErrorLogonFailure)
                    return new(AccountPasswordStatus.InvalidPassword, sid, "Invalid username or password.");
                if (error != ProcessLaunchNative.Win32ErrorLogonTypeNotGranted)
                    return new(AccountPasswordStatus.Failed, sid, $"Credential validation failed: {new Win32Exception(error).Message}");
            }

            return new(AccountPasswordStatus.Succeeded, sid, null);
        });
    }

    public AccountPasswordResult ChangeAccountPassword(string sid, ProtectedString oldPassword, ProtectedString newPassword)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            if (user == null)
                throw new InvalidOperationException($"Account not found for SID {sid}.");

            int result = oldPassword.UseUnicodeSnapshot(oldSnapshot =>
            {
                IntPtr oldPasswordPtr = oldSnapshot.DangerousGetIntPtr();
                return newPassword.UseUnicodeSnapshot<int>(newSnapshot =>
                    AccountPasswordNative.NetUserChangePassword(
                        Environment.MachineName,
                        user.SamAccountName,
                        oldPasswordPtr,
                        newSnapshot.DangerousGetIntPtr()));
            });
            if (result != 0)
                throw new Win32Exception(result);

            log.Info($"Changed password for account with SID {sid}");
            return new(AccountPasswordStatus.Succeeded, sid, null);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Win32Exception ex)
        {
            var status = ex.NativeErrorCode switch
            {
                5 => AccountPasswordStatus.AccessDenied,
                86 => AccountPasswordStatus.InvalidPassword,
                _ => AccountPasswordStatus.PolicyRejected
            };
            return new(status, sid, ex.Message);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to change password for SID {sid}", ex);
            return new(AccountPasswordStatus.Failed, sid, ex.Message);
        }
    }

    public AccountPasswordResult AdminResetAccountPassword(string sid, ProtectedString newPassword)
    {
        IntPtr structPtr = IntPtr.Zero;
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            if (user == null)
                throw new InvalidOperationException($"Account not found for SID {sid}.");

            structPtr = Marshal.AllocHGlobal(IntPtr.Size);
            int result = newPassword.UseUnicodeSnapshot(snapshot =>
            {
                Marshal.WriteIntPtr(structPtr, snapshot.DangerousGetIntPtr());
                return WindowsAccountNative.NetUserSetInfo(null, user.SamAccountName, 1003, structPtr, out _);
            });
            if (result != 0)
                throw new Win32Exception(result);

            log.Info($"Admin-reset password for account with SID {sid}");
            return new(AccountPasswordStatus.Succeeded, sid, null);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Win32Exception ex)
        {
            var status = ex.NativeErrorCode switch
            {
                5 => AccountPasswordStatus.AccessDenied,
                86 => AccountPasswordStatus.InvalidPassword,
                _ => AccountPasswordStatus.PolicyRejected
            };
            return new(status, sid, ex.Message);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to admin-reset password for SID {sid}", ex);
            return new(AccountPasswordStatus.Failed, sid, ex.Message);
        }
        finally
        {
            if (structPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(structPtr);
        }
    }
}

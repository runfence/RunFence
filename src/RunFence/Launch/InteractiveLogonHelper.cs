using System.ComponentModel;
using RunFence.Account;
using RunFence.Core;

namespace RunFence.Launch;

/// <summary>
/// Encapsulates the grant-retry-cleanup pattern for SeInteractiveLogonRight.
/// Both Process.Start-based and LogonUser-based launch paths encounter
/// ERROR_LOGON_TYPE_NOT_GRANTED when the account lacks interactive logon rights.
/// This helper temporarily grants the right, retries the action, and revokes it
/// only if it was not held before.
/// </summary>
public class InteractiveLogonHelper(ILsaRightsHelper lsaRights, ILoggingService log) : IInteractiveLogonHelper
{
    /// <summary>
    /// Executes <paramref name="action"/>. If it throws a <see cref="Win32Exception"/> with
    /// <c>ERROR_LOGON_TYPE_NOT_GRANTED</c> and a non-null <paramref name="username"/> is provided,
    /// temporarily grants <c>SeInteractiveLogonRight</c> and retries. The right is revoked in a
    /// finally block only if it was not already held before the grant.
    /// </summary>
    /// <typeparam name="T">Return type of the action.</typeparam>
    /// <param name="domain">Domain for SID resolution (may be null).</param>
    /// <param name="username">Username for SID resolution. Pass null for current-account launches to skip retry logic.</param>
    /// <param name="action">The action to attempt, which may throw <see cref="Win32Exception"/>.</param>
    /// <returns>The result of <paramref name="action"/> on success.</returns>
    public T RunWithLogonRetry<T>(string? domain, string? username, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonTypeNotGranted && username != null)
        {
            var sidBytes = lsaRights.TryResolveSidBytes(domain, username);
            if (sidBytes == null)
                throw;

            var existingRights = lsaRights.EnumerateAccountRights(sidBytes);
            var alreadyHeld = existingRights.Contains(LsaRightsHelper.SeInteractiveLogonRight, StringComparer.OrdinalIgnoreCase);

            log.Info($"Granting SeInteractiveLogonRight to {username} for launch retry");
            lsaRights.AddAccountRights(sidBytes, [LsaRightsHelper.SeInteractiveLogonRight]);
            try
            {
                return action();
            }
            finally
            {
                if (!alreadyHeld)
                    try
                    {
                        lsaRights.RemoveAccountRights(sidBytes, [LsaRightsHelper.SeInteractiveLogonRight]);
                    }
                    catch (Exception revokeEx)
                    {
                        log.Error("Failed to revoke SeInteractiveLogonRight after launch retry", revokeEx);
                    }
            }
        }
    }
}
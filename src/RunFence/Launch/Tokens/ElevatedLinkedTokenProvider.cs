using RunFence.Core;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires the elevated linked token for a UAC-filtered interactive logon token by briefly
/// impersonating SYSTEM (winlogon.exe) to gain SeTcbPrivilege, which causes
/// GetTokenInformation(TOKEN_LINKED_TOKEN) to return a usable PRIMARY elevated token instead
/// of a SecurityIdentification impersonation token.
/// </summary>
public class ElevatedLinkedTokenProvider(
    ILoggingService log,
    ISystemPrivilegeRunner systemPrivilegeRunner)
    : IElevatedLinkedTokenProvider
{
    /// <summary>
    /// Acquires the elevated linked token for <paramref name="hFilteredToken"/>.
    /// The returned handle is owned by the caller and must be closed via CloseHandle.
    /// </summary>
    /// <param name="hFilteredToken">
    /// A UAC-filtered (non-elevated) interactive logon token whose linked token is the
    /// elevated counterpart. Must have TOKEN_QUERY access.
    /// </param>
    /// <returns>A PRIMARY elevated token handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when winlogon.exe is not found or the linked token could not be acquired.
    /// </exception>
    /// <exception cref="Win32Exception">
    /// Thrown on P/Invoke failure during SYSTEM token acquisition or impersonation.
    /// </exception>
    public IntPtr AcquireElevatedLinkedToken(IntPtr hFilteredToken)
    {
        log.Debug("ElevatedLinkedTokenProvider: querying linked token under SYSTEM impersonation");
        var hElevated = systemPrivilegeRunner.RunWithPrivileges([TokenPrivilegeHelper.SeTcbPrivilege], () =>
        {
            var linkedToken = LinkedTokenHelper.TryGetLinkedToken(hFilteredToken);
            if (linkedToken == IntPtr.Zero)
                throw new InvalidOperationException("Failed to acquire elevated linked token under SYSTEM impersonation");
            return linkedToken;
        });
        log.Debug("ElevatedLinkedTokenProvider: elevated linked token acquired");
        return hElevated;
    }
}

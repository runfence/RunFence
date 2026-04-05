using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using RunFence.Core;

namespace RunFence.Account;

public class AccountValidationService(ILoggingService log) : IAccountValidationService
{
    public void ValidateNotCurrentAccount(string sid, string action)
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        if (string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"Blocked attempt to {action} current account: {sid}");
            throw new InvalidOperationException($"Cannot {action} the current account.");
        }
    }

    public void ValidateNotLastAdmin(string sid, string action)
    {
        if (IsLastAdminAccount(sid))
        {
            log.Warn($"Blocked attempt to {action} last admin account: {sid}");
            throw new InvalidOperationException($"Cannot {action} the last administrator account.");
        }
    }

    public void ValidateNotInteractiveUser(string sid, string action)
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null &&
            string.Equals(sid, interactiveSid, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"Blocked attempt to {action} interactive user: {sid}");
            throw new InvalidOperationException($"Cannot {action} the currently logged-in account.");
        }
    }

    public void ValidateNoRunningProcesses(string sid, string action)
    {
        var processNames = GetProcessesRunningAsSid(sid);
        if (processNames.Count > 0)
        {
            var list = string.Join(", ", processNames.Take(10));
            if (processNames.Count > 10)
                list += $" (and {processNames.Count - 10} more)";
            log.Warn($"Blocked attempt to {action} account {sid} with {processNames.Count} running processes");
            throw new InvalidOperationException(
                $"Cannot {action} this account while it has running processes:\n{list}");
        }
    }

    public List<string> GetProcessesRunningAsSid(string targetSid)
    {
        var processNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var sid = NativeTokenHelper.TryGetProcessOwnerSid(proc.Handle);
                if (sid != null && string.Equals(sid.Value, targetSid, StringComparison.OrdinalIgnoreCase))
                {
                    var name = proc.ProcessName;
                    if (seen.Add(name))
                        processNames.Add(name);
                }
            }
            catch
            {
                /* skip inaccessible processes */
            }
            finally
            {
                proc.Dispose();
            }
        }

        return processNames;
    }

    private bool IsLastAdminAccount(string sid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, "S-1-5-32-544");
            if (adminGroup == null)
                return false;

            using var members = adminGroup.GetMembers();
            var adminMembers = members
                .OfType<UserPrincipal>()
                .Where(u => u.Enabled != false)
                .Select(u => u.Sid?.Value)
                .Where(s => s != null)
                .ToList();

            return adminMembers.Count <= 1 &&
                   adminMembers.Any(s => string.Equals(s, sid, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            log.Error("Failed to check admin accounts", ex);
            return true; // Err on the side of safety
        }
    }
}
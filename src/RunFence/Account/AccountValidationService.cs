using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account;

public class AccountValidationService(ILoggingService log, ILocalGroupMembershipService localGroupMembership, IProcessListService processListService) : IAccountValidationService
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
        var processes = processListService.GetProcessesForSid(targetSid);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processNames = new List<string>();

        foreach (var proc in processes)
        {
            using (proc)
            {
                var name = proc.ExecutablePath != null
                    ? Path.GetFileNameWithoutExtension(proc.ExecutablePath)
                    : $"pid:{proc.Pid}";
                if (seen.Add(name))
                    processNames.Add(name);
            }
        }

        return processNames;
    }

    private bool IsLastAdminAccount(string sid)
    {
        try
        {
            var members = localGroupMembership.GetMembersOfGroup("S-1-5-32-544");
            var enabledAdminSids = members
                .Where(m => !localGroupMembership.IsLocalGroup(m.Sid))
                .Where(m => localGroupMembership.IsUserAccountEnabled(m.Username))
                .Select(m => m.Sid)
                .ToList();

            return enabledAdminSids.Count <= 1 &&
                   enabledAdminSids.Any(s => string.Equals(s, sid, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            log.Error("Failed to check admin accounts", ex);
            return true; // Err on the side of safety
        }
    }
}

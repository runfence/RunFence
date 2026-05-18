using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;

namespace RunFence.Account;

public class AccountValidationService(ILoggingService log, ILocalGroupMembershipService localGroupMembership, IProcessListService processListService) : IAccountValidationService
{
    public void ValidateNotCurrentAccount(string sid, string action)
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        if (SidComparer.SidEquals(sid, currentSid))
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
            SidComparer.SidEquals(sid, interactiveSid))
        {
            log.Warn($"Blocked attempt to {action} interactive user: {sid}");
            throw new InvalidOperationException($"Cannot {action} the currently logged-in account.");
        }
    }

    public IReadOnlyList<ProcessInfo> GetRunningProcesses(string targetSid)
        => processListService.GetProcessesForSid(targetSid)
            .OrderBy(GetProcessDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Pid)
            .ToList();

    public List<string> GetProcessesRunningAsSid(string targetSid)
    {
        var processes = GetRunningProcesses(targetSid);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processNames = new List<string>();

        foreach (var proc in processes)
        {
            var name = GetProcessDisplayName(proc);
            if (seen.Add(name))
                processNames.Add(name);
        }

        return processNames;
    }

    private static string GetProcessDisplayName(ProcessInfo proc)
        => proc.ExecutablePath != null
            ? Path.GetFileNameWithoutExtension(proc.ExecutablePath)
            : $"pid:{proc.Pid}";

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
                   enabledAdminSids.Any(s => SidComparer.SidEquals(s, sid));
        }
        catch (Exception ex)
        {
            log.Error("Failed to check admin accounts", ex);
            return true; // Err on the side of safety
        }
    }
}

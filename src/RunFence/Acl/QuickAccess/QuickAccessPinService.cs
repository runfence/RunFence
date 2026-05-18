using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Acl.QuickAccess;

public class QuickAccessPinService(
    ISessionProvider sessionProvider,
    ILaunchFacade facade,
    ILoggingService log)
    : IQuickAccessPinService
{
    private static readonly string PinHelperExe =
        Path.Combine(AppContext.BaseDirectory, "RunFence.PinHelper.exe");

    public void PinFolders(string accountSid, IReadOnlyList<string> paths)
    {
        if (!IsEligible(accountSid))
            return;

        var filteredPaths = paths.Where(Directory.Exists).ToList();
        if (filteredPaths.Count == 0)
            return;

        log.Info($"Pinning {filteredPaths.Count} folder(s) for {accountSid}: {string.Join(", ", filteredPaths)}");
        var pinTask = Task.Run(() => TryLaunchProcess(filteredPaths, unpin: false, accountSid));
        pinTask.ContinueWith(t => log.Warn($"QuickAccessPinService: pin task faulted for {accountSid}: {t.Exception!.InnerException?.Message ?? t.Exception.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void UnpinFolders(string accountSid, IReadOnlyList<string> paths)
    {
        if (!IsEligible(accountSid))
            return;

        if (paths.Count == 0)
            return;

        log.Info($"Unpinning {paths.Count} folder(s) for {accountSid}: {string.Join(", ", paths)}");
        var unpinTask = Task.Run(() => TryLaunchProcess(paths, unpin: true, accountSid));
        unpinTask.ContinueWith(t => log.Warn($"QuickAccessPinService: unpin task faulted for {accountSid}: {t.Exception!.InnerException?.Message ?? t.Exception.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void PinAllGrantedFolders()
    {
        var session = sessionProvider.GetSession();
        foreach (var account in session.Database.Accounts)
        {
            var folderPaths = account.Grants
                .Where(g => g is { IsTraverseOnly: false, IsDeny: false } && Directory.Exists(g.Path))
                .Select(g => g.Path).ToList();
            if (folderPaths.Count > 0)
                PinFolders(account.Sid, folderPaths);
        }
    }

    private bool IsEligible(string accountSid)
    {
        if (AclHelper.IsContainerSid(accountSid) || AclHelper.IsLowIntegritySid(accountSid))
            return false;
        if (string.Equals(accountSid, SidResolutionHelper.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(accountSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void TryLaunchProcess(IReadOnlyList<string> paths, bool unpin, string accountSid)
    {
        try
        {
            var flag = unpin ? "--unpin-folders" : "--pin-folders";
            var cmdArgs = CommandLineHelper.JoinArgs(new[] { flag }.Concat(paths));
            var target = new ProcessLaunchTarget(PinHelperExe, Arguments: cmdArgs, SuppressStartupFeedback: true);
            using var launch = facade.LaunchFile(target, new AccountLaunchIdentity(accountSid));
            var warning = LaunchExecutionWarningFormatter.Format("The Quick Access helper", launch);
            if (warning != null)
                log.Warn(warning);
            log.Info($"PinHelper ({(unpin ? "unpin" : "pin")}) succeeded for {accountSid}");
        }
        catch (Exception ex)
        {
            log.Warn($"PinHelper ({(unpin ? "unpin" : "pin")}) failed for {accountSid}: {ex.Message}");
        }
    }
}

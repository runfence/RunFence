using RunFence.Acl;
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
        _ = Task.Run(() => TryLaunchProcess(filteredPaths, unpin: false, accountSid));
    }

    public void UnpinFolders(string accountSid, IReadOnlyList<string> paths)
    {
        if (!IsEligible(accountSid))
            return;

        if (paths.Count == 0)
            return;

        log.Info($"Unpinning {paths.Count} folder(s) for {accountSid}: {string.Join(", ", paths)}");
        _ = Task.Run(() => TryLaunchProcess(paths, unpin: true, accountSid));
    }

    public void PinAllGrantedFolders()
    {
        var session = sessionProvider.GetSession();
        foreach (var account in session.Database.Accounts)
        {
            var folderPaths = account.Grants
                .Where(g => !g.IsTraverseOnly && !g.IsDeny && Directory.Exists(g.Path))
                .Select(g => g.Path).ToList();
            if (folderPaths.Count > 0)
                PinFolders(account.Sid, folderPaths);
        }
    }

    private bool IsEligible(string accountSid)
    {
        if (AclHelper.IsContainerSid(accountSid))
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
            var target = new ProcessLaunchTarget(PinHelperExe, Arguments: cmdArgs);
            facade.LaunchFile(target, new AccountLaunchIdentity(accountSid));
            log.Info($"PinHelper ({(unpin ? "unpin" : "pin")}) succeeded for {accountSid}");
        }
        catch (Exception ex)
        {
            log.Warn($"PinHelper ({(unpin ? "unpin" : "pin")}) failed for {accountSid}: {ex.Message}");
        }
    }
}

using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Security;

namespace RunFence.Acl.QuickAccess;

public class QuickAccessPinService(
    ISessionProvider sessionProvider,
    ICredentialEncryptionService encryptionService,
    ISidResolver sidResolver,
    IProcessLaunchService processLaunchService,
    IPermissionGrantService permissionGrantService,
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

        var creds = TryGetCredentials(accountSid);
        if (creds == null)
            return;

        permissionGrantService.EnsureExeDirectoryAccess(PinHelperExe, accountSid);
        log.Info($"Pinning {filteredPaths.Count} folder(s) for {accountSid}: {string.Join(", ", filteredPaths)}");

        var credsValue = creds.Value;
        _ = Task.Run(() => TryLaunchProcess(credsValue, filteredPaths, unpin: false, accountSid));
    }

    public void UnpinFolders(string accountSid, IReadOnlyList<string> paths)
    {
        if (!IsEligible(accountSid))
            return;

        if (paths.Count == 0)
            return;

        var creds = TryGetCredentials(accountSid);
        if (creds == null)
            return;

        permissionGrantService.EnsureExeDirectoryAccess(PinHelperExe, accountSid);
        log.Info($"Unpinning {paths.Count} folder(s) for {accountSid}: {string.Join(", ", paths)}");

        var credsValue = creds.Value;
        _ = Task.Run(() => TryLaunchProcess(credsValue, paths, unpin: true, accountSid));
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
        if (accountSid.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(accountSid, SidResolutionHelper.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(accountSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private LaunchCredentials? TryGetCredentials(string accountSid)
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        var creds = CredentialHelper.DecryptAndResolve(
            accountSid, session.CredentialStore, encryptionService, scope.Data, sidResolver, null,
            out var status);
        return status == CredentialLookupStatus.Success ? creds : null;
    }

    private void TryLaunchProcess(LaunchCredentials creds, IReadOnlyList<string> paths, bool unpin, string accountSid)
    {
        try
        {
            var flag = unpin ? "--unpin-folders" : "--pin-folders";
            var args = CommandLineHelper.JoinArgs(new[] { flag }.Concat(paths));
            processLaunchService.LaunchExe(new ProcessLaunchTarget(PinHelperExe, Arguments: args), creds);
            log.Info($"PinHelper ({(unpin ? "unpin" : "pin")}) succeeded for {accountSid}");
        }
        catch (Exception ex)
        {
            log.Warn($"PinHelper ({(unpin ? "unpin" : "pin")}) failed for {accountSid}: {ex.Message}");
        }
    }
}

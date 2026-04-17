using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using IProfileRepairHelper = RunFence.Account.IProfileRepairHelper;

namespace RunFence.Launch;

public class ProcessLauncher(
    IAccountProcessLauncher accountProcessLauncher,
    ILaunchCredentialsLookup credentialsLookup,
    IProfileRepairHelper profileRepairHelper,
    IFolderHandlerService folderHandlerService,
    IAssociationAutoSetService associationAutoSetService,
    IAppContainerProcessLauncher appContainerLauncher,
    ILoggingService log)
    : IProcessLauncher, ILaunchIdentityAcceptor<ProcessInfo?>
{
    public ProcessInfo? Launch(LaunchIdentity identity, ProcessLaunchTarget target)
        => identity.Visit(this, target);

    public ProcessInfo? Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var creds = identity.Credentials ?? credentialsLookup.GetBySid(identity.Sid);
        try
        {
            var resolved = identity with { Credentials = creds };

            var r = profileRepairHelper.ExecuteWithProfileRepair(() => accountProcessLauncher.Launch(target, resolved), identity.Sid);
            log.Info($"Launched {target.ExePath} as {creds.Username ?? (creds.TokenSource == LaunchTokenSource.InteractiveUser ? "interactive user" : "current account")}");
            folderHandlerService.Register(identity.Sid);
            associationAutoSetService.AutoSetForUser(identity.Sid);
            return r;
        }
        finally
        {
            if (identity.Credentials == null)
                creds.Password?.Dispose();
        }
    }

    public ProcessInfo? Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return appContainerLauncher.LaunchFile(target, identity);
    }
}

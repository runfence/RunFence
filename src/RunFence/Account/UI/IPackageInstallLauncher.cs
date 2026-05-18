using RunFence.Launch;

namespace RunFence.Account.UI;

public interface IPackageInstallLauncher
{
    PackageInstallLaunchResult Launch(string scriptPath, AccountLaunchIdentity identity);
}

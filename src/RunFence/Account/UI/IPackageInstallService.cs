using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Account.UI;

public interface IPackageInstallService
{
    bool IsPackageInstalled(InstallablePackage package, string sid);
    IReadOnlyList<string> InstallPackages(IReadOnlyList<InstallablePackage> packages, AccountLaunchIdentity identity);
    Task WaitForInstallCompletionAsync(string sid, TimeSpan? timeout = null, CancellationToken ct = default);
    void CleanupStaleScripts();
}

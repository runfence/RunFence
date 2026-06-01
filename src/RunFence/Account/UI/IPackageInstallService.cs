using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Account.UI;

public interface IPackageInstallService
{
    bool IsPackageInstalled(InstallablePackage package, string sid);
    Task<IReadOnlyList<string>> InstallPackagesAsync(
        IReadOnlyList<InstallablePackage> packages,
        AccountLaunchIdentity identity,
        CancellationToken cancellationToken);
    Task WaitForInstallCompletionAsync(string sid, TimeSpan? timeout = null, CancellationToken ct = default);
    void CleanupStaleScripts();
}

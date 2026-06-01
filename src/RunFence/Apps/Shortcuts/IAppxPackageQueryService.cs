namespace RunFence.Apps.Shortcuts;

public interface IAppxPackageQueryService
{
    IReadOnlyList<RegisteredAppxPackage> QueryPackages();
}

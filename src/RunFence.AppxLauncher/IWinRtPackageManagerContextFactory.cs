namespace RunFence.AppxLauncher;

public interface IWinRtPackageManagerContextFactory
{
    IWinRtPackageManagerContext Create();
}

public interface IWinRtPackageManagerContext : IDisposable
{
    void RegisterPackageByFamilyName(string packageFamilyName);
}


namespace RunFence.AppxLauncher;

public sealed class WinRtPackageRegistration
    : IAppxPackageRegistration
{
    private readonly IWinRtPackageManagerContextFactory _contextFactory;

    public WinRtPackageRegistration(IWinRtPackageManagerContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public void RegisterPackageByFamilyName(string packageFamilyName)
    {
        using var context = _contextFactory.Create();
        context.RegisterPackageByFamilyName(packageFamilyName);
    }
}

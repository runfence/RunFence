using RunFence.Core;

namespace RunFence.Persistence;

public class ProductionConfigPaths : IConfigPaths
{
    public string ConfigFilePath => Path.Combine(Constants.RoamingAppDataDir, "config.dat");
    public string CredentialsFilePath => Path.Combine(Constants.LocalAppDataDir, "credentials.dat");
    public string LocalDataDir => Constants.LocalAppDataDir;
}

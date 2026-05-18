using RunFence.Core;

namespace RunFence.Persistence;

public class ProductionConfigPaths : IConfigPaths
{
    public string ConfigFilePath => Path.Combine(PathConstants.RoamingAppDataDir, "config.dat");
    public string CredentialsFilePath => Path.Combine(PathConstants.LocalAppDataDir, "credentials.dat");
    public string LicenseFilePath => Path.Combine(PathConstants.RoamingAppDataDir, "license.dat");
    public string RememberPinFilePath => Path.Combine(PathConstants.LocalAppDataDir, "startkey.dat");
}

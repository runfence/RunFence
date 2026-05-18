namespace RunFence.Persistence;

public interface IConfigPaths
{
    string ConfigFilePath { get; }
    string CredentialsFilePath { get; }
    string LicenseFilePath { get; }
    string RememberPinFilePath { get; }
}

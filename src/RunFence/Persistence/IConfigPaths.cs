namespace RunFence.Persistence;

public interface IConfigPaths
{
    string ConfigFilePath { get; }
    string CredentialsFilePath { get; }
    string LocalDataDir { get; }
}

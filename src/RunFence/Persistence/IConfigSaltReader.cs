namespace RunFence.Persistence;

public interface IConfigSaltReader
{
    byte[]? TryGetConfigSalt();
    byte[]? TryGetConfigSaltFromPath(string configPath);
    byte[]? TryGetAppConfigSalt(string configPath);
    byte[]? TryGetAppConfigSaltFromPath(string configPath);
}

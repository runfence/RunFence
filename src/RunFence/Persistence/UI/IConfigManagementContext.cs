namespace RunFence.Persistence.UI;

public interface IConfigManagementContext
{
    (bool success, string? errorMessage) LoadApps(string configPath);
    bool UnloadApps(string configPath);
}
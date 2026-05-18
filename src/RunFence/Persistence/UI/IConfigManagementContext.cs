namespace RunFence.Persistence.UI;

public interface IConfigManagementContext
{
    LoadAppsResult LoadApps(string configPath);
    LoadAppsResult LoadAppConfigBackup(string configPath);
    bool UnloadApps(string configPath);
}

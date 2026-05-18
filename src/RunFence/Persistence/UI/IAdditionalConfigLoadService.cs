namespace RunFence.Persistence.UI;

public interface IAdditionalConfigLoadService
{
    LoadAppsResult LoadApps(string configPath);
    bool UnloadApps(string configPath);
}

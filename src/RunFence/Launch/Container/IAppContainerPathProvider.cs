namespace RunFence.Launch.Container;

public interface IAppContainerPathProvider
{
    string GetContainersRootPath();
    string GetContainerDataPath(string profileName);
}

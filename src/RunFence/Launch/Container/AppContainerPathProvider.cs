namespace RunFence.Launch.Container;

public class AppContainerPathProvider : IAppContainerPathProvider
{
    public string GetContainersRootPath()
        => AppContainerPaths.GetContainersRootPath();

    public string GetContainerDataPath(string profileName)
        => AppContainerPaths.GetContainerDataPath(profileName);
}

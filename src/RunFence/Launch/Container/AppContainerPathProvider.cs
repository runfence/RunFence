using RunFence.Acl;

namespace RunFence.Launch.Container;

public class AppContainerPathProvider(IProgramDataKnownPathResolver programDataKnownPathResolver) : IAppContainerPathProvider
{
    public string GetContainersRootPath()
        => programDataKnownPathResolver.GetDirectoryPath(ProgramDataPolicies.Ac);

    public string GetContainerDataPath(string profileName)
        => Path.Combine(GetContainersRootPath(), profileName);
}

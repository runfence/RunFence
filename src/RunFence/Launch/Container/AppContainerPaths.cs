using RunFence.Core;

namespace RunFence.Launch.Container;

/// <summary>
/// Shared path derivation for AppContainer data folders.
/// Used by both AppContainerService and AppContainerEnvironmentSetup.
/// </summary>
public static class AppContainerPaths
{
    public static string GetContainersRootPath()
        => Path.Combine(Constants.ProgramDataDir, "AC");

    public static string GetContainerDataPath(string profileName)
        => Path.Combine(GetContainersRootPath(), profileName);
}
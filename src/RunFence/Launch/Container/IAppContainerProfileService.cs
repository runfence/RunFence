using RunFence.Core.Models;

namespace RunFence.Launch.Container;

/// <summary>
/// Profile lifecycle and identity operations for AppContainers.
/// </summary>
public interface IAppContainerProfileService
{
    /// <summary>Creates the OS AppContainer profile and data folder.</summary>
    void CreateProfile(AppContainerEntry entry);

    /// <summary>Deletes the OS profile and data folder. Disables loopback first if needed.</summary>
    void DeleteProfile(string name, bool hadLoopback = false);

    /// <summary>Ensures the AppContainer profile exists (creates if missing).</summary>
    void EnsureProfile(AppContainerEntry entry);

    /// <summary>Returns true if the OS profile exists for the given name.</summary>
    bool ProfileExists(string name);

    /// <summary>Returns the AppContainer SID string derived from the profile name.</summary>
    string GetSid(string name);

    /// <summary>Returns the container data folder path.</summary>
    string GetContainerDataPath(string name);
}
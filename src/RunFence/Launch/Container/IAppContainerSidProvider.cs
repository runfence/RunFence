namespace RunFence.Launch.Container;

/// <summary>
/// Derives the AppContainer SID string for a given container name.
/// Encapsulates the native P/Invoke and pointer lifecycle.
/// </summary>
public interface IAppContainerSidProvider
{
    /// <summary>
    /// Derives and returns the SID string for <paramref name="containerName"/>.
    /// Throws <see cref="InvalidOperationException"/> or <see cref="System.ComponentModel.Win32Exception"/>
    /// on failure.
    /// </summary>
    string GetSidString(string containerName);
}

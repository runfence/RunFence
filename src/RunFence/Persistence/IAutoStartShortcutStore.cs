namespace RunFence.Persistence;

/// <summary>
/// Owns the startup shortcut paths and file-system operations for the auto-start feature.
/// Isolates path calculation and I/O from <see cref="AutoStartService"/> so tests never
/// read or modify the real user Startup folder.
/// </summary>
public interface IAutoStartShortcutStore
{
    /// <summary>Path to RunFence.exe inside <see cref="AppContext.BaseDirectory"/>.</summary>
    string RunFenceExePath { get; }

    /// <summary>Path to the RunFence-autostart.cmd wrapper inside <see cref="AppContext.BaseDirectory"/>.</summary>
    string CmdWrapperPath { get; }

    /// <summary>
    /// The primary shortcut path to write when enabling auto-start
    /// (interactive-user startup folder).
    /// </summary>
    string PrimaryShortcutPath { get; }

    /// <summary>
    /// All startup shortcut paths that should be checked or cleaned up
    /// (interactive-user startup folder and fallback startup folder).
    /// Duplicate paths (when interactive-user folder equals the fallback) appear only once.
    /// </summary>
    IReadOnlyCollection<string> ShortcutPaths { get; }

    /// <summary>Returns <c>true</c> when the file at <paramref name="path"/> exists.</summary>
    bool FileExists(string path);

    /// <summary>Deletes the file at <paramref name="path"/>.</summary>
    void DeleteFile(string path);
}

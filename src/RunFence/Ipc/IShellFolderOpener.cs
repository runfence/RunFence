namespace RunFence.Ipc;

/// <summary>
/// Opens a folder in Explorer via shell APIs. Must be called on an STA (UI) thread.
/// </summary>
public interface IShellFolderOpener
{
    /// <summary>
    /// Attempts to open the folder at <paramref name="canonicalPath"/> in Explorer.
    /// Returns <c>true</c> on success; <c>false</c> with a diagnostic <paramref name="errorMessage"/> on failure.
    /// </summary>
    bool TryOpen(string canonicalPath, out string? errorMessage);
}
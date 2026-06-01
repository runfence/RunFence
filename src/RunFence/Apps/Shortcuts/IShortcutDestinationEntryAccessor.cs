using Microsoft.Win32.SafeHandles;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutDestinationEntryAccessor
{
    ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath);
    void DeleteExistingDestination(string shortcutPath);
    SafeFileHandle OpenNewDestination(string shortcutPath, uint desiredAccess);
}

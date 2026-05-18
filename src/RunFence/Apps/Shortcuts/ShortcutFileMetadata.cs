using System.Security.AccessControl;

namespace RunFence.Apps.Shortcuts;

public sealed record ShortcutFileMetadata(
    FileSecurity Security,
    FileAttributes Attributes,
    DateTime CreationTimeUtc,
    DateTime LastWriteTimeUtc,
    DateTime LastAccessTimeUtc);

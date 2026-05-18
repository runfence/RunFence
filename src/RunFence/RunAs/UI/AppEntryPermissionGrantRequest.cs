using System.Security.AccessControl;

namespace RunFence.RunAs.UI;

public sealed record AppEntryPermissionGrantRequest(
    string TargetSid,
    string Path,
    FileSystemRights Rights,
    bool PinFolderAfterGrant);

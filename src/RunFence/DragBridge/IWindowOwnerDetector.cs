using System.Security.Principal;

namespace RunFence.DragBridge;

public readonly record struct WindowOwnerInfo(
    SecurityIdentifier Sid,
    int IntegrityLevel,
    bool IsInRestrictedJob,
    SecurityIdentifier? AppContainerSid = null)
{
    public bool IsAppContainer => AppContainerSid != null;
}

public interface IWindowOwnerDetector
{
    WindowOwnerInfo? GetForegroundWindowOwnerInfo();
    WindowOwnerInfo? GetDragSourceOrForegroundOwnerInfo();
}

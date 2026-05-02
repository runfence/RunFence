using System.Security.Principal;

namespace RunFence.DragBridge;

public record struct WindowOwnerInfo(SecurityIdentifier Sid, int IntegrityLevel, bool IsInRestrictedJob);

public interface IWindowOwnerDetector
{
    WindowOwnerInfo? GetForegroundWindowOwnerInfo();
    WindowOwnerInfo? GetDragSourceOrForegroundOwnerInfo();
}
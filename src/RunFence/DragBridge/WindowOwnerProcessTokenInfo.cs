using System.Security.Principal;

namespace RunFence.DragBridge;

public readonly record struct WindowOwnerProcessTokenInfo(
    SecurityIdentifier OwnerSid,
    SecurityIdentifier? AppContainerSid,
    int? IntegrityLevel,
    bool? IsElevated = null);

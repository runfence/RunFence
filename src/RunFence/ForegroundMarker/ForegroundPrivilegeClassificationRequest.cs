namespace RunFence.ForegroundMarker;

public readonly record struct ForegroundPrivilegeClassificationRequest(
    long RequestId,
    IntPtr TrackedWindowHandle,
    uint PrivilegeSubjectProcessId,
    long EnabledGeneration);

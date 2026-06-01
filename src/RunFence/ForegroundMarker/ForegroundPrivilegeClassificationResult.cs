namespace RunFence.ForegroundMarker;

public sealed record ForegroundPrivilegeClassificationResult(
    long RequestId,
    IntPtr TrackedWindowHandle,
    long EnabledGeneration,
    bool IsVisible,
    ForegroundPrivilegeMarkerKind? Kind,
    ForegroundPrivilegeTooltipMode? TooltipMode,
    uint PrivilegeSubjectProcessId,
    long? PrivilegeSubjectCreationTimeUtcTicks,
    bool IsCacheable,
    bool IsStale,
    ForegroundPrivilegeMarkerMetadata? Metadata = null)
{
    public static ForegroundPrivilegeClassificationResult Hidden(
        ForegroundPrivilegeClassificationRequest request,
        bool isCacheable = false,
        ForegroundPrivilegeTooltipMode? tooltipMode = null) =>
        new(
            request.RequestId,
            request.TrackedWindowHandle,
            request.EnabledGeneration,
            false,
            null,
            tooltipMode,
            request.PrivilegeSubjectProcessId,
            null,
            isCacheable,
            false);

    public static ForegroundPrivilegeClassificationResult Visible(
        ForegroundPrivilegeClassificationRequest request,
        ForegroundPrivilegeMarkerKind kind,
        bool isCacheable = true) =>
        new(
            request.RequestId,
            request.TrackedWindowHandle,
            request.EnabledGeneration,
            true,
            kind,
            kind switch
            {
                ForegroundPrivilegeMarkerKind.Basic => null,
                ForegroundPrivilegeMarkerKind.Isolated => ForegroundPrivilegeTooltipMode.Isolated,
                ForegroundPrivilegeMarkerKind.LowIL => ForegroundPrivilegeTooltipMode.LowIL,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            },
            request.PrivilegeSubjectProcessId,
            null,
            isCacheable,
            false);

    public ForegroundPrivilegeClassificationResult WithIdentity(
        ForegroundPrivilegeClassificationRequest request,
        long? creationTimeUtcTicks,
        bool isCacheable,
        bool isStale) =>
        this with
        {
            RequestId = request.RequestId,
            TrackedWindowHandle = request.TrackedWindowHandle,
            EnabledGeneration = request.EnabledGeneration,
            PrivilegeSubjectProcessId = request.PrivilegeSubjectProcessId,
            PrivilegeSubjectCreationTimeUtcTicks = creationTimeUtcTicks,
            IsCacheable = isCacheable,
            IsStale = isStale,
        };
}

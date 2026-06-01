namespace RunFence.ForegroundMarker;

public sealed record CachedForegroundPrivilegeClassification(
    bool IsVisible,
    ForegroundPrivilegeMarkerKind? Kind,
    ForegroundPrivilegeTooltipMode? TooltipMode,
    uint PrivilegeSubjectProcessId,
    long PrivilegeSubjectCreationTimeUtcTicks,
    bool IsCacheable)
{
    public static CachedForegroundPrivilegeClassification FromResult(ForegroundPrivilegeClassificationResult result)
    {
        if (!result.PrivilegeSubjectCreationTimeUtcTicks.HasValue)
        {
            throw new InvalidOperationException("Cached classification requires process creation time identity.");
        }

        if (result.IsVisible && !result.Kind.HasValue)
        {
            throw new InvalidOperationException("Visible classification requires marker kind.");
        }

        if (!result.IsVisible && result.Kind.HasValue)
        {
            throw new InvalidOperationException("Hidden classification cannot carry marker kind.");
        }

        return new CachedForegroundPrivilegeClassification(
            result.IsVisible,
            result.IsVisible ? result.Kind : null,
            result.TooltipMode,
            result.PrivilegeSubjectProcessId,
            result.PrivilegeSubjectCreationTimeUtcTicks.Value,
            result.IsCacheable);
    }

    public ForegroundPrivilegeClassificationResult ToResult(ForegroundPrivilegeClassificationRequest request)
    {
        if (!IsVisible)
        {
            return ForegroundPrivilegeClassificationResult.Hidden(request, IsCacheable, TooltipMode) with
            {
                PrivilegeSubjectProcessId = PrivilegeSubjectProcessId,
                PrivilegeSubjectCreationTimeUtcTicks = PrivilegeSubjectCreationTimeUtcTicks,
            };
        }

        if (!Kind.HasValue)
        {
            throw new InvalidOperationException("Visible cached classification requires marker kind.");
        }

        return ForegroundPrivilegeClassificationResult.Visible(request, Kind.Value, IsCacheable) with
        {
            TooltipMode = TooltipMode,
            PrivilegeSubjectProcessId = PrivilegeSubjectProcessId,
            PrivilegeSubjectCreationTimeUtcTicks = PrivilegeSubjectCreationTimeUtcTicks,
        };
    }
}

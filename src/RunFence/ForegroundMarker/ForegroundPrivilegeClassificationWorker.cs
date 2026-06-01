using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundPrivilegeClassificationWorker(
    IProcessCreationTimeReader processCreationTimeReader,
    ForegroundPrivilegeMarkerMetadataResolver metadataResolver,
    ForegroundPrivilegeClassifier classifier) : IForegroundPrivilegeClassificationWorker
{
    private readonly object _cacheLock = new();
    private CachedForegroundPrivilegeClassification? _cachedResult;

    public Task<ForegroundPrivilegeClassificationResult> ClassifyAsync(
        ForegroundPrivilegeClassificationRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ClassifyCore(request, cancellationToken),
            cancellationToken);

    private ForegroundPrivilegeClassificationResult ClassifyCore(
        ForegroundPrivilegeClassificationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasInitialCreationTime = processCreationTimeReader.TryGetProcessCreationTimeUtcTicks(
            request.PrivilegeSubjectProcessId,
            out var initialCreationTimeUtcTicks);

        ForegroundPrivilegeClassificationResult result;
        if (hasInitialCreationTime && TryGetCachedResult(request, initialCreationTimeUtcTicks, out var cachedResult))
        {
            result = FinalizeResult(request, cachedResult, hasInitialCreationTime, initialCreationTimeUtcTicks);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = classifier.Classify(request);
            cancellationToken.ThrowIfCancellationRequested();
            result = FinalizeResult(request, result, hasInitialCreationTime, initialCreationTimeUtcTicks);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsStale || result.PrivilegeSubjectProcessId == 0)
            return result with { Metadata = null };

        if (!result.PrivilegeSubjectCreationTimeUtcTicks.HasValue)
        {
            return result with
            {
                Metadata = ForegroundPrivilegeMarkerMetadata.CreateFallback(result.PrivilegeSubjectProcessId),
            };
        }

        var metadata = metadataResolver.Resolve(result.PrivilegeSubjectProcessId);
        if (!processCreationTimeReader.TryGetProcessCreationTimeUtcTicks(
                result.PrivilegeSubjectProcessId,
                out var currentCreationTimeUtcTicks)
            || currentCreationTimeUtcTicks != result.PrivilegeSubjectCreationTimeUtcTicks.Value)
        {
            ClearCache();
            return result with
            {
                IsCacheable = false,
                IsStale = true,
                Metadata = null,
            };
        }

        return result with { Metadata = metadata };
    }

    private ForegroundPrivilegeClassificationResult FinalizeResult(
        ForegroundPrivilegeClassificationRequest request,
        ForegroundPrivilegeClassificationResult result,
        bool hasInitialCreationTime,
        long initialCreationTimeUtcTicks)
    {
        if (!hasInitialCreationTime)
            return result.WithIdentity(request, null, isCacheable: false, isStale: false);

        if (!processCreationTimeReader.TryGetProcessCreationTimeUtcTicks(
                request.PrivilegeSubjectProcessId,
                out var finalCreationTimeUtcTicks)
            || finalCreationTimeUtcTicks != initialCreationTimeUtcTicks)
        {
            ClearCache();
            return result.WithIdentity(request, initialCreationTimeUtcTicks, isCacheable: false, isStale: true);
        }

        var finalizedResult = result.WithIdentity(
            request,
            initialCreationTimeUtcTicks,
            isCacheable: result.IsCacheable,
            isStale: false);

        if (finalizedResult.IsCacheable)
            UpdateCache(finalizedResult);

        return finalizedResult;
    }

    private bool TryGetCachedResult(
        ForegroundPrivilegeClassificationRequest request,
        long creationTimeUtcTicks,
        out ForegroundPrivilegeClassificationResult cachedResult)
    {
        lock (_cacheLock)
        {
            if (_cachedResult != null
                && _cachedResult.PrivilegeSubjectProcessId == request.PrivilegeSubjectProcessId
                && _cachedResult.PrivilegeSubjectCreationTimeUtcTicks == creationTimeUtcTicks)
            {
                cachedResult = _cachedResult.ToResult(request);
                return true;
            }

            cachedResult = null!;
            return false;
        }
    }

    private void UpdateCache(ForegroundPrivilegeClassificationResult result)
    {
        if (!result.PrivilegeSubjectCreationTimeUtcTicks.HasValue)
            return;

        lock (_cacheLock)
        {
            _cachedResult = CachedForegroundPrivilegeClassification.FromResult(result);
        }
    }

    private void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedResult = null;
        }
    }
}

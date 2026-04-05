using RunFence.Core;

namespace RunFence.Infrastructure;

public class InteractiveUserDesktopProvider : IInteractiveUserDesktopProvider
{
    private readonly ISidResolver _sidResolver;
    private string? _cachedDesktopPath;

    public InteractiveUserDesktopProvider(ISidResolver sidResolver)
    {
        _sidResolver = sidResolver;
    }

    public string? GetDesktopPath()
    {
        if (_cachedDesktopPath != null)
            return _cachedDesktopPath;
        var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
        if (interactiveSid == null)
            return null;
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var isCurrentAccount = string.Equals(interactiveSid, currentSid, StringComparison.OrdinalIgnoreCase);
        _cachedDesktopPath = _sidResolver.TryGetDesktopPath(interactiveSid, isCurrentAccount);
        return _cachedDesktopPath;
    }
}
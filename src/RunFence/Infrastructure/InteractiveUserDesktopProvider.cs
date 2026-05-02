using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Infrastructure;

public class InteractiveUserDesktopProvider(IProfilePathResolver profilePathResolver, IInteractiveUserResolver interactiveUserResolver) : IInteractiveUserDesktopProvider
{
    private string? _cachedDesktopPath;
    private string? _cachedTaskBarPath;

    /// <summary>
    /// Invalidates the desktop and taskbar path caches. Should be called when the
    /// interactive user changes (e.g., fast user switching) so the next access
    /// resolves fresh paths.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedDesktopPath = null;
        _cachedTaskBarPath = null;
    }

    public string? GetDesktopPath()
    {
        if (_cachedDesktopPath != null)
            return _cachedDesktopPath;
        var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
        if (interactiveSid == null)
            return null;
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var isCurrentAccount = SidComparer.SidEquals(interactiveSid, currentSid);
        _cachedDesktopPath = profilePathResolver.TryGetDesktopPath(interactiveSid, isCurrentAccount);
        return _cachedDesktopPath;
    }

    public string? GetTaskBarPath()
    {
        if (_cachedTaskBarPath != null)
            return _cachedTaskBarPath;
        var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
        if (interactiveSid == null)
            return null;
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var isCurrentAccount = SidComparer.SidEquals(interactiveSid, currentSid);
        _cachedTaskBarPath = profilePathResolver.TryGetTaskBarPath(interactiveSid, isCurrentAccount);
        return _cachedTaskBarPath;
    }
}

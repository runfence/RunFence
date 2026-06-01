using RunFence.Acl.Permissions;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;

namespace RunFence.Launch;

// We resolve associations manually because sometimes Windows redirects launch to interactive user,
// either when we use elevated launch or when there is no existing association (less likely).
// It's hard to determine the exact rule because it differs for different file types, uwp/win32, elevated/unelevated/normal, present/absent association.
// If we can't find association for target user, we try take association from interactive user
// but still run it under target user.
// Therefore, we can't reliably use rundll32 or cmd /c start to resolve association via windows mechanism.
public class LaunchTargetResolver(
    IInteractiveUserResolver interactiveUserResolver,
    AssociationLaunchCandidateResolver associationLaunchCandidateResolver,
    UiThreadDatabaseAccessor dbAccessor,
    LaunchHiveLeaseCoordinator launchHiveLeaseCoordinator,
    ShortcutTargetResolver shortcutTargetResolver,
    ILoggingService log)
    : ILaunchTargetResolver
{
    private readonly ILoggingService _log = log;

    public TraversePathResult TraversePath(string path, LaunchIdentity identity)
        => TraversePath(path, identity, CreateDatabaseSnapshot());

    public TraversePathResult TraversePath(string path, LaunchIdentity identity, AppDatabase databaseSnapshot)
    {
        string? shortcutArgs = null;
        string? shortcutWorkDir = null;
        var extension = Path.GetExtension(path);

        if (Directory.Exists(path))
            return new TraversePathResult(path, null, null, true, extension);

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = shortcutTargetResolver.TryResolveShortcut(path, databaseSnapshot.Apps);
            if (resolved == null)
                throw new InvalidOperationException(
                    "Could not resolve shortcut target. The shortcut may be broken or reference a removed app entry.");

            if (resolved.Value.Context.IsAlreadyManaged
                && resolved.Value.Context.ManagedApp?.AccountSid is { } managedSid
                && identity is AccountLaunchIdentity { AssociationResolutionPolicy: not AssociationResolutionPolicy.AllowAccountRedirection } accountIdentity
                && !string.Equals(managedSid, accountIdentity.Sid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Managed shortcut targets app '{resolved.Value.Context.ManagedApp!.Id}' which belongs to a different account.");
            }

            path = resolved.Value.ResolvedPath;
            shortcutArgs = resolved.Value.ShortcutArgs;
            shortcutWorkDir = resolved.Value.ShortcutWorkingDirectory;
            extension = Path.GetExtension(path);

            if (Directory.Exists(path))
                return new TraversePathResult(path, shortcutArgs, shortcutWorkDir, true, extension);
        }

        try
        {
            path = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Path doesn't exist or is inaccessible — not a reparse point
        }

        if (Directory.Exists(path))
            return new TraversePathResult(path, shortcutArgs, shortcutWorkDir, true, extension);

        return new TraversePathResult(path, shortcutArgs, shortcutWorkDir, false, extension);
    }

    public LaunchTargetResolutionResult ResolveFileHandler(LaunchIdentity identity, ProcessLaunchTarget target, string? extension = null)
        => ResolveFileHandler(identity, target, CreateDatabaseSnapshot(), extension);

    public LaunchTargetResolutionResult ResolveFileHandler(
        LaunchIdentity identity,
        ProcessLaunchTarget target,
        AppDatabase databaseSnapshot,
        string? extension = null)
    {
        if (ProcessLaunchHelper.CanLaunchDirect(target, extension))
            return new LaunchTargetResolutionResult(target, LaunchResolutionKind.Direct, null, _log);

        var scriptTarget = ProcessLaunchHelper.TryWrapForScriptLaunch(target, extension);
        if (scriptTarget != null)
            return new LaunchTargetResolutionResult(scriptTarget, LaunchResolutionKind.Script, null, _log);

        var request = AssociationResolutionRequest.ForFile(target, extension);
        return identity.Visit(new ResolutionVisitor(this, request, databaseSnapshot), target);
    }

    public LaunchTargetResolutionResult ResolveUrlHandler(LaunchIdentity identity, string url)
        => ResolveUrlHandler(identity, url, CreateDatabaseSnapshot());

    public LaunchTargetResolutionResult ResolveUrlHandler(LaunchIdentity identity, string url, AppDatabase databaseSnapshot)
    {
        if (!ProcessLaunchHelper.ValidateUrlScheme(url, out var error))
            throw new InvalidOperationException($"URL scheme blocked: {error}");
        var request = AssociationResolutionRequest.ForUrl(url);
        return identity.Visit(new ResolutionVisitor(this, request, databaseSnapshot), null);
    }

    private LaunchTargetResolutionResult ResolveAssociation(
        AssociationResolutionRequest request,
        AppDatabase databaseSnapshot,
        AccountLaunchIdentity identity)
    {
        IDisposable? launchedHiveLease = null;
        IDisposable? interactiveHiveLease = null;

        try
        {
            launchedHiveLease = launchHiveLeaseCoordinator.EnsureHiveLoaded(identity.Sid);
            var launchedTarget = associationLaunchCandidateResolver.ResolveForSid(
                identity.Sid,
                request,
                databaseSnapshot,
                identity.AssociationResolutionPolicy,
                rejectUserProfileHandlers: false);
            if (launchedTarget != null)
            {
                _log.Debug(
                    $"LaunchTargetResolver: resolved {request.Kind} '{request.RawArgument}' for launched SID {identity.Sid} to '{launchedTarget.ExePath}'.");
                var hiveLease = launchHiveLeaseCoordinator.TakeCombinedLease(ref launchedHiveLease, ref interactiveHiveLease);
                return new LaunchTargetResolutionResult(launchedTarget, LaunchResolutionKind.Handler, hiveLease, _log);
            }

            var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
            if (launchHiveLeaseCoordinator.ShouldUseInteractiveFallback(identity.Sid, interactiveSid))
            {
                interactiveHiveLease = launchHiveLeaseCoordinator.EnsureHiveLoaded(interactiveSid!);
                var interactiveTarget = associationLaunchCandidateResolver.ResolveForSid(
                interactiveSid!,
                request,
                databaseSnapshot,
                identity.AssociationResolutionPolicy,
                    rejectUserProfileHandlers: true);
                if (interactiveTarget != null)
                {
                    _log.Debug(
                        $"LaunchTargetResolver: resolved {request.Kind} '{request.RawArgument}' via interactive SID {interactiveSid} fallback to '{interactiveTarget.ExePath}'.");
                    var hiveLease = launchHiveLeaseCoordinator.TakeCombinedLease(ref launchedHiveLease, ref interactiveHiveLease);
                    return new LaunchTargetResolutionResult(interactiveTarget, LaunchResolutionKind.Handler, hiveLease, _log);
                }
            }

            if (identity.PrivilegeLevel == PrivilegeLevel.HighestAllowed
                && identity.AssociationResolutionPolicy != AssociationResolutionPolicy.AllowAccountRedirection)
            {
                _log.Warn(
                    $"LaunchTargetResolver: no usable association handler found for {request.Kind} '{request.RawArgument}' under HighestAllowed for SID {identity.Sid}.");
                throw new AssociationResolutionException(
                    $"No usable association handler found for '{request.RawArgument}'.");
            }

            var fallbackLease = launchHiveLeaseCoordinator.TakeCombinedLease(ref launchedHiveLease, ref interactiveHiveLease);
            _log.Debug(
                $"LaunchTargetResolver: falling back to legacy wrapped launch for {request.Kind} '{request.RawArgument}' and SID {identity.Sid}.");
            return new LaunchTargetResolutionResult(request.GetFallbackTarget(), LaunchResolutionKind.ShellWrapped, fallbackLease, _log);
        }
        catch
        {
            try
            {
                interactiveHiveLease?.Dispose();
            }
            finally
            {
                launchedHiveLease?.Dispose();
            }
            throw;
        }
    }

    private AppDatabase CreateDatabaseSnapshot()
        => dbAccessor.CreateSnapshot();

    private sealed class ResolutionVisitor(
        LaunchTargetResolver owner,
        AssociationResolutionRequest request,
        AppDatabase databaseSnapshot)
        : ILaunchIdentityAcceptor<LaunchTargetResolutionResult>
    {
        public LaunchTargetResolutionResult Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
            => owner.ResolveAssociation(request, databaseSnapshot, identity);

        public LaunchTargetResolutionResult Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
            => new(request.GetFallbackTarget(), LaunchResolutionKind.ShellWrapped, null, owner._log);
    }

}

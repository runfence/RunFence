using RunFence.Acl.Permissions;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

// We resolve associations manually because sometimes Windows redirects launch to interactive user,
// either when we use elevated launch or when there is no existing association (less likely).
// It's hard to determine the exact rule because it differs for different file types, uwp/win32, elevated/unelevated/normal, present/absent association.
// If we can't find association for target user, we try take association from interactive user
// but still run it under target user.
// Therefore, we can't reliably use rundll32 or cmd /c start to resolve association via windows mechanism.
public class LaunchTargetResolver(
    IInteractiveUserResolver interactiveUserResolver,
    AssociationRegistryResolver associationRegistryResolver,
    AssociationCommandMaterializer associationCommandMaterializer,
    IAssociationLaunchResolver associationLaunchResolver,
    IUiThreadInvoker uiThreadInvoker,
    LaunchHiveLeaseCoordinator launchHiveLeaseCoordinator,
    ShortcutTargetResolver shortcutTargetResolver,
    ISessionProvider sessionProvider,
    ILoggingService log)
    : ILaunchTargetResolver
{
    private readonly ILoggingService _log = log;

    public TraversePathResult TraversePath(string path, LaunchIdentity identity)
    {
        string? shortcutArgs = null;
        string? shortcutWorkDir = null;

        if (Directory.Exists(path))
            return new TraversePathResult(path, null, null, true);

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = shortcutTargetResolver.TryResolveShortcut(path, sessionProvider.GetSession().Database.Apps);
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

            if (Directory.Exists(path))
                return new TraversePathResult(path, shortcutArgs, shortcutWorkDir, true);
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
            return new TraversePathResult(path, shortcutArgs, shortcutWorkDir, true);

        return new TraversePathResult(path, shortcutArgs, shortcutWorkDir, false);
    }

    public LaunchTargetResolutionResult ResolveFileHandler(LaunchIdentity identity, ProcessLaunchTarget target)
    {
        if (ProcessLaunchHelper.CanLaunchDirect(target))
            return new LaunchTargetResolutionResult(target, LaunchResolutionKind.Direct, null, _log);

        var scriptTarget = ProcessLaunchHelper.TryWrapForScriptLaunch(target);
        if (scriptTarget != null)
            return new LaunchTargetResolutionResult(scriptTarget, LaunchResolutionKind.Script, null, _log);

        var request = AssociationResolutionRequest.ForFile(target);
        return identity.Visit(new ResolutionVisitor(this, request), target);
    }

    public LaunchTargetResolutionResult ResolveUrlHandler(LaunchIdentity identity, string url)
    {
        if (!ProcessLaunchHelper.ValidateUrlScheme(url, out var error))
            throw new InvalidOperationException($"URL scheme blocked: {error}");
        var request = AssociationResolutionRequest.ForUrl(url);
        return identity.Visit(new ResolutionVisitor(this, request), null);
    }

    private LaunchTargetResolutionResult ResolveAssociation(AssociationResolutionRequest request, AccountLaunchIdentity identity)
    {
        IDisposable? launchedHiveLease = null;
        IDisposable? interactiveHiveLease = null;

        try
        {
            launchedHiveLease = launchHiveLeaseCoordinator.EnsureHiveLoaded(identity.Sid);
            var launchedTarget = ResolveForSid(
                identity.Sid,
                request,
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
                var interactiveTarget = ResolveForSid(
                    interactiveSid!,
                    request,
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
                throw new InvalidOperationException($"No usable association handler found for '{request.RawArgument}'.");
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

    private ProcessLaunchTarget? ResolveForSid(
        string sid,
        AssociationResolutionRequest request,
        AssociationResolutionPolicy associationResolutionPolicy,
        bool rejectUserProfileHandlers)
    {
        var candidates = request.Kind switch
        {
            AssociationLaunchKind.File => associationRegistryResolver.ResolveFileCandidates(
                sid,
                request.FileTarget!,
                rejectUserProfileHandlers),
            AssociationLaunchKind.Url => associationRegistryResolver.ResolveUrlCandidates(
                sid,
                request.RawArgument,
                rejectUserProfileHandlers),
            _ => []
        };

        foreach (var candidate in candidates)
        {
            var materialized = associationCommandMaterializer.TryMaterialize(candidate);
            if (materialized == null)
                continue;

            if (materialized.LauncherAssociation != null)
            {
                // This intentionally validates only unsafe account redirection, not full launcher setup.
                // A broken launcher command is a configuration problem handled by later launch validation or failure behavior.
                if (associationResolutionPolicy != AssociationResolutionPolicy.AllowAccountRedirection)
                {
                    var resolved = uiThreadInvoker.Invoke(() =>
                        associationLaunchResolver.Resolve(
                            sessionProvider.GetSession().Database,
                            materialized.LauncherAssociation,
                            materialized.LauncherArgument!,
                            callerIdentity: null,
                            callerSid: candidate.ResolutionSid,
                            identityFromImpersonation: true));

                    if (resolved.App != null
                        && !string.Equals(resolved.App.AccountSid, candidate.ResolutionSid, StringComparison.OrdinalIgnoreCase))
                    {
                        LogCommandResolutionReject(candidate, materialized.MaterializedCommand,
                            $"RunFence association launcher resolves to account SID '{resolved.App.AccountSid}' instead of '{candidate.ResolutionSid}'");
                        continue;
                    }
                }

                _log.Debug(
                    $"LaunchTargetResolver: accepted {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
                    + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)} as RunFence association launcher."
                    + $" RegistryCommand='{candidate.RegistryCommand}'. MaterializedCommand='{materialized.MaterializedCommand}'.");
            }

            return materialized.Target;
        }

        return null;
    }

    private void LogCommandResolutionReject(
        AssociationRegistryCommandCandidate candidate,
        string? command,
        string reason)
        => _log.Debug(
            $"LaunchTargetResolver: rejected {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
            + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)}: {reason}. Command='{command ?? string.Empty}'.");

    private sealed class ResolutionVisitor(LaunchTargetResolver owner, AssociationResolutionRequest request)
        : ILaunchIdentityAcceptor<LaunchTargetResolutionResult>
    {
        public LaunchTargetResolutionResult Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
            => owner.ResolveAssociation(request, identity);

        public LaunchTargetResolutionResult Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
            => new(request.GetFallbackTarget(), LaunchResolutionKind.ShellWrapped, null, owner._log);
    }

    private sealed record AssociationResolutionRequest(
        AssociationLaunchKind Kind,
        string RawArgument,
        ProcessLaunchTarget? FileTarget)
    {
        public ProcessLaunchTarget GetFallbackTarget()
            => Kind == AssociationLaunchKind.File
                ? ProcessLaunchHelper.WrapForShellLaunch(FileTarget!)
                : ProcessLaunchHelper.BuildUrlLaunchTarget(RawArgument);

        public static AssociationResolutionRequest ForFile(ProcessLaunchTarget originalTarget)
            => new(AssociationLaunchKind.File, originalTarget.ExePath, originalTarget);

        public static AssociationResolutionRequest ForUrl(string url)
            => new(AssociationLaunchKind.Url, url, null);
    }

    private enum AssociationLaunchKind
    {
        File,
        Url
    }
}

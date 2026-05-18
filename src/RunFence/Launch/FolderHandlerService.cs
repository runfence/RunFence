using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class FolderHandlerService(
    ILoggingService log,
    IPathGrantService pathGrantService,
    ILocalGroupMembershipService localGroupMembership,
    FolderHandlerRegistryStore registryStore,
    FolderHandlerRegistrationRollback registrationRollback,
    FolderHandlerSidLockProvider sidLockProvider,
    string? launcherPathOverride = null)
    : IFolderHandlerService
{
    public bool IsRegistered(string accountSid) => registryStore.IsRegistered(accountSid);

    public FolderHandlerRegistrationResult Register(string accountSid)
    {
        using var sidLock = sidLockProvider.Acquire(accountSid);
        if (registryStore.IsRegistered(accountSid))
        {
            log.Info($"FolderHandlerService: already registered for {accountSid}, skipping");
            return new FolderHandlerRegistrationResult();
        }

        if (SidResolutionHelper.IsSystemSid(accountSid))
        {
            log.Info("FolderHandlerService: skipping registration for SYSTEM account");
            return new FolderHandlerRegistrationResult();
        }

        if (string.Equals(accountSid, SidResolutionHelper.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
        {
            log.Info($"FolderHandlerService: skipping registration for interactive user {accountSid}");
            return new FolderHandlerRegistrationResult();
        }

        if (localGroupMembership.GetGroupsForUser(accountSid)
                .Any(g => string.Equals(g.Sid, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase)))
        {
            log.Info($"FolderHandlerService: skipping registration for admin account {accountSid}");
            return new FolderHandlerRegistrationResult();
        }

        var launcherPath = launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Warn($"FolderHandlerService: launcher not found at {launcherPath}, skipping registration");
            return new FolderHandlerRegistrationResult();
        }

        log.Info($"FolderHandlerService: registering folder handler for {accountSid}");
        var effects = new FolderHandlerRegistrationEffects(accountSid, launcherPath);
        var warnings = new List<string>();
        string? saveFailureMessage = null;
        var saveFailureEncountered = false;

        try
        {
            var commandValue = $"\"{launcherPath}\" --open-folder \"%V\"";
            registryStore.Register(accountSid, commandValue);
            effects.RegistryWritten = true;
            effects.SidTracked = registryStore.IsRegistered(accountSid);

            var launcherDir = Path.GetDirectoryName(launcherPath);
            if (!string.IsNullOrEmpty(launcherDir))
            {
                TryEnsureRegistrationAccess(
                    accountSid,
                    launcherDir,
                    effects,
                    warnings,
                    isAccountGrant: true,
                    out var accountSaveFailure);
                TryEnsureRegistrationAccess(
                    AclHelper.LowIntegritySid,
                    launcherDir,
                    effects,
                    warnings,
                    isAccountGrant: false,
                    out var lowIntegritySaveFailure);
                saveFailureEncountered = accountSaveFailure || lowIntegritySaveFailure;
            }

            if (!saveFailureEncountered)
                effects.RunOnceWritten = registryStore.WriteRunOnce(accountSid, launcherPath);

            ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST,
                IntPtr.Zero, IntPtr.Zero);

            if (saveFailureEncountered)
            {
                var warningText = warnings.Count == 0
                    ? "Folder handler registration completed with a grant save failure."
                    : string.Join("\n", warnings);
                log.Warn($"FolderHandlerService: registration completed with {warnings.Count} warning(s): {warningText}");
                saveFailureMessage = $"Folder handler registration completed with {warnings.Count} warning(s): {warningText}";
            }
        }
        catch (GrantOperationException ex) when (!IsSaveFailureStep(ex.Step) && !saveFailureEncountered)
        {
            log.Error($"FolderHandlerService: registration failed for {accountSid}", ex);
            registrationRollback.Rollback(effects);
            throw;
        }
        catch (Exception ex) when (saveFailureEncountered)
        {
            var warningText = saveFailureMessage ?? $"Folder handler registration encountered an additional warning for {accountSid}: {ex.Message}";
            log.Warn(warningText);
            throw new InvalidOperationException(warningText, ex);
        }
        catch (Exception ex)
        {
            log.Error($"FolderHandlerService: registration failed for {accountSid}", ex);
            registrationRollback.Rollback(effects);
            throw;
        }

        if (saveFailureMessage != null)
            throw new InvalidOperationException(saveFailureMessage);

        if (warnings.Count > 0)
            log.Warn($"FolderHandlerService: registration completed with {warnings.Count} warning(s) for {accountSid}");

        log.Info($"FolderHandlerService: registration complete for {accountSid}");
        return new FolderHandlerRegistrationResult(warnings);
    }

    public void Unregister(string accountSid)
    {
        using var sidLock = sidLockProvider.Acquire(accountSid);
        log.Info($"FolderHandlerService: unregistering folder handler for {accountSid}");

        try
        {
            registryStore.Unregister(accountSid);
            ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST,
                IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            log.Error($"FolderHandlerService: unregistration failed for {accountSid}", ex);
        }
    }

    public void UnregisterAll()
    {
        foreach (var sid in registryStore.GetRegisteredSids())
            Unregister(sid);
    }

    public void CleanupStaleRegistrations()
    {
        log.Info("FolderHandlerService: cleaning up stale registrations.");
        try
        {
            var cleanedAny = registryStore.CleanupStaleEntries();
            if (cleanedAny)
            {
                ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST,
                    IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerService: cleanup failed: {ex.Message}");
        }
    }

    private void TryEnsureRegistrationAccess(
        string sid,
        string launcherDir,
        FolderHandlerRegistrationEffects effects,
        List<string> warnings,
        bool isAccountGrant,
        out bool saveFailure)
    {
        saveFailure = false;
        try
        {
            var result = pathGrantService.EnsureAccess(
                sid,
                launcherDir,
                FileSystemRights.ReadAndExecute,
                confirm: null,
                unelevated: true);
            if (isAccountGrant)
            {
                effects.AccountGrantApplied = result.GrantApplied;
                effects.AccountTraverseApplied = result.TraverseApplied;
            }
            else
            {
                effects.LowIntegrityGrantApplied = result.GrantApplied;
                effects.LowIntegrityTraverseApplied = result.TraverseApplied;
            }

            AppendGrantWarnings(warnings, result.Warnings);
        }
        catch (GrantOperationException ex) when (IsSaveFailureStep(ex.Step))
        {
            saveFailure = true;
            warnings.Add(GrantApplyFailureFormatter.Format(new GrantApplyFailure(ex.Step, ex.Path, ex.ConfigPath, ex.Cause)));
            return;
        }
    }

    private static void AppendGrantWarnings(List<string> warnings, IReadOnlyList<GrantApplyWarning> grantWarnings)
    {
        foreach (var warning in grantWarnings)
            warnings.Add(GrantApplyFailureFormatter.Format(warning));
    }

    private static bool IsSaveFailureStep(GrantApplyFailureStep step)
        => GrantApplyFailureFormatter.IsSaveFailureStep(step);
}

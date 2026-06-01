using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationWorkflow(
    ILoggingService log,
    FolderHandlerSidPolicy sidPolicy,
    FolderHandlerTrackedSidState trackedSidState,
    FolderHandlerRegistrationAccessService registrationAccessService,
    FolderHandlerRunOnceMaintenance runOnceMaintenance,
    FolderHandlerRegistrationWriter registrationWriter,
    FolderHandlerRegistrationRollback registrationRollback,
    FolderHandlerSidLockProvider sidLockProvider,
    string? launcherPathOverride = null)
{
    public bool IsRegistered(string accountSid)
        => registrationWriter.HasOwnedRegistration(accountSid);

    public FolderHandlerRegistrationResult Register(string accountSid)
    {
        using var sidLock = sidLockProvider.Acquire(accountSid);
        var hadOwnedRegistrationBeforeCall = IsRegistered(accountSid);
        if (!sidPolicy.ShouldKeepRegistrationForSid(accountSid))
        {
            trackedSidState.Remove(accountSid);
            return new FolderHandlerRegistrationResult();
        }

        var launcherPath = launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Warn($"FolderHandlerRegistrationWorkflow: launcher not found at {launcherPath}, skipping registration");
            return new FolderHandlerRegistrationResult();
        }

        log.Info($"FolderHandlerRegistrationWorkflow: registering folder handler for {accountSid}");
        var effects = new FolderHandlerRegistrationEffects(accountSid, launcherPath);
        var warnings = new List<string>();
        string? saveFailureMessage = null;
        var saveFailureEncountered = false;
        FolderHandlerRegistrationMaintenanceResult? maintenanceResult = null;

        try
        {
            var launcherDir = Path.GetDirectoryName(launcherPath);
            if (!string.IsNullOrEmpty(launcherDir))
                saveFailureEncountered = registrationAccessService.EnsureLauncherDirectoryAccess(accountSid, launcherDir, effects, warnings);

            trackedSidState.ExecuteLocked(() =>
            {
                maintenanceResult = registrationWriter.EnsureOwnedRegistration(
                    accountSid,
                    launcherPath,
                    runOnceMaintenance.BuildCommandLine(launcherPath));
                effects.RegistrationChangeSet = maintenanceResult.ChangeSet;
                trackedSidState.Add(accountSid);

                if (maintenanceResult.RegistryChanged || maintenanceResult.RunOnceChanged)
                {
                    ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST,
                        IntPtr.Zero, IntPtr.Zero);
                }
            });

            if (saveFailureEncountered)
            {
                var warningText = warnings.Count == 0
                    ? "Folder handler registration completed with a grant save failure."
                    : string.Join("\n", warnings);
                log.Warn($"FolderHandlerRegistrationWorkflow: registration completed with {warnings.Count} warning(s): {warningText}");
                saveFailureMessage = $"Folder handler registration completed with {warnings.Count} warning(s): {warningText}";
            }
        }
        catch (GrantOperationException ex) when (!GrantApplyFailureFormatter.IsSaveFailureStep(ex.Step) && !saveFailureEncountered)
        {
            log.Error($"FolderHandlerRegistrationWorkflow: registration failed for {accountSid}", ex);
            registrationRollback.Rollback(effects);
            throw;
        }
        catch (FolderHandlerRegistrationMaintenanceException ex)
        {
            log.Error($"FolderHandlerRegistrationWorkflow: registration failed for {accountSid}", ex);
            effects.RegistrationChangeSet = ex.MaintenanceResult.ChangeSet;
            trackedSidState.ExecuteLocked(() =>
            {
                registrationRollback.Rollback(effects);
                if (ex.MaintenanceResult.HadOwnedRegistrationBeforeCall || hadOwnedRegistrationBeforeCall)
                    trackedSidState.Add(accountSid);
                else
                    trackedSidState.Remove(accountSid);
            });
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"FolderHandlerRegistrationWorkflow: registration failed for {accountSid}", ex);
            trackedSidState.ExecuteLocked(() =>
            {
                registrationRollback.Rollback(effects);
                if (maintenanceResult?.HadOwnedRegistrationBeforeCall == true || hadOwnedRegistrationBeforeCall)
                    trackedSidState.Add(accountSid);
                else
                    trackedSidState.Remove(accountSid);
            });
            throw;
        }

        if (saveFailureMessage != null)
            throw new InvalidOperationException(saveFailureMessage);

        if (warnings.Count > 0)
            log.Warn($"FolderHandlerRegistrationWorkflow: registration completed with {warnings.Count} warning(s) for {accountSid}");

        log.Info($"FolderHandlerRegistrationWorkflow: registration complete for {accountSid}");
        return new FolderHandlerRegistrationResult(warnings);
    }

    public void Unregister(string accountSid)
    {
        using var sidLock = sidLockProvider.Acquire(accountSid);
        log.Info($"FolderHandlerRegistrationWorkflow: unregistering folder handler for {accountSid}");

        try
        {
            trackedSidState.ExecuteLocked(() =>
            {
                registrationWriter.Unregister(accountSid);
                trackedSidState.Remove(accountSid);
                ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST,
                    IntPtr.Zero, IntPtr.Zero);
            });
        }
        catch (Exception ex)
        {
            log.Error($"FolderHandlerRegistrationWorkflow: unregistration failed for {accountSid}", ex);
        }
    }
}

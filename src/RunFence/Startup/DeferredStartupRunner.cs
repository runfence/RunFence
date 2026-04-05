using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Runs all deferred startup work after the main form handle is created.
/// Orchestrates three parallel work streams: stale registration cleanup,
/// feature activation, and startup enforcement with post-enforcement steps.
/// </summary>
public class DeferredStartupRunner(
    IFolderHandlerService folderHandlerService,
    StartupFeatureActivator featureActivator,
    StartupEnforcementRunner enforcementRunner,
    ISessionProvider sessionProvider,
    ILoggingService log)
{
    /// <summary>
    /// Runs deferred startup work on the UI thread. Must be called via BeginInvoke after
    /// the main form handle is created, so that Task.Run streams can marshal back via BeginInvoke.
    /// </summary>
    public void Run(MainForm mainForm)
    {
        // Pre-work on the UI thread before any background tasks:
        // Fix AppEntry property inconsistencies on the live database, then snapshot.
        // FixAppEntryDefaults must run before CreateSnapshot because CreateSnapshot does
        // a shallow copy — fixes must be visible to both the snapshot and the live database.
        enforcementRunner.FixAppEntryDefaults();
        var snapshot = sessionProvider.GetSession().Database.CreateSnapshot();

        // Stream 1: fire-and-forget stale folder handler cleanup (registry enumeration/deletion)
        Task.Run(() =>
        {
            try
            {
                folderHandlerService.CleanupStaleRegistrations();
            }
            catch (Exception ex)
            {
                log.Warn($"Stale folder handler cleanup failed: {ex.Message}");
            }
        });

        // Stream 2: fire-and-forget context menu and handler registration sync (registry reads/writes).
        // Sequential within the task to avoid concurrent registry access from the same service.
        Task.Run(() =>
        {
            try
            {
                featureActivator.ActivateContextMenus();
                featureActivator.SyncHandlerRegistrations();
            }
            catch (Exception ex)
            {
                log.Warn($"Background startup registration failed: {ex.Message}");
            }
        });

        // Stream 3: enforcement chain — background I/O then UI-thread result application.
        Task.Run(() =>
        {
            try
            {
                var result = enforcementRunner.EnforceOnSnapshot(snapshot);
                mainForm.BeginInvoke(async () =>
                {
                    try
                    {
                        await enforcementRunner.ApplyEnforcementResult(result);
                        enforcementRunner.ProcessExpiredContainersAtStartup();
                        enforcementRunner.ProcessExpiredAccounts();
                        enforcementRunner.GrantUnlockDirAccess();
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex.ToString());
                    }
                });
            }
            catch (Exception ex)
            {
                log.Warn($"Deferred startup enforcement failed: {ex.Message}");
                mainForm.BeginInvoke(() => MessageBox.Show(
                    $"Startup enforcement failed:\n{ex.Message}\n\nACL rules may not be fully applied.",
                    "RunFence — Enforcement Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }
        });
    }
}
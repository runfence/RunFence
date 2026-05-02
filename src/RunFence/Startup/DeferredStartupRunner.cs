using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Runs all deferred startup work after the main form handle is created.
/// Orchestrates three parallel work streams: cleanup (stale folder registrations + install scripts),
/// feature activation, and startup enforcement.
/// </summary>
public class DeferredStartupRunner(
    IFolderHandlerService folderHandlerService,
    StartupFeatureActivator featureActivator,
    StartupEnforcementRunner enforcementRunner,
    ISessionProvider sessionProvider,
    PackageInstallService packageInstallService,
    StartupOptions startupOptions,
    ILoggingService log)
{
    /// <summary>
    /// Runs deferred startup work on the UI thread. Must be called via BeginInvoke after
    /// the main form handle is created, so that Task.Run streams can marshal back via BeginInvoke.
    /// </summary>
    public void Run(MainForm mainForm)
    {
        // Pre-work on the UI thread before any background tasks:
        // Refresh container SIDs when the interactive user has changed, fix AppEntry property
        // inconsistencies on the live database, then snapshot.
        // Both methods must run before CreateSnapshot because CreateSnapshot does a shallow copy
        // — their changes must be visible to both the snapshot and the live database.
        enforcementRunner.RefreshContainerSidsIfUserChanged();
        enforcementRunner.FixAppEntryDefaults();
        var snapshot = sessionProvider.GetSession().Database.CreateSnapshot();

        // Stream 1: fire-and-forget stale folder handler cleanup (registry enumeration/deletion)
        // and stale install script cleanup.
        var stream1 = Task.Run(() =>
        {
            folderHandlerService.CleanupStaleRegistrations();
            packageInstallService.CleanupStaleScripts();
        });
        stream1.ContinueWith(t => log.Error("DeferredStartupRunner: stream 1 (cleanup) faulted", t.Exception!.InnerException ?? t.Exception),
            TaskContinuationOptions.OnlyOnFaulted);

        // Stream 2: fire-and-forget context menu and handler registration sync (registry reads/writes).
        // Sequential within the task to avoid concurrent registry access from the same service.
        var stream2 = Task.Run(() =>
        {
            featureActivator.ActivateContextMenus(snapshot);
            featureActivator.SyncHandlerRegistrations(snapshot);
        });
        stream2.ContinueWith(t => log.Error("DeferredStartupRunner: background startup registration faulted", t.Exception!.InnerException ?? t.Exception),
            TaskContinuationOptions.OnlyOnFaulted);

        // Stream 3: enforcement chain — background I/O then UI-thread result application.
        var stream3 = Task.Run(() =>
        {
            try
            {
                var result = enforcementRunner.EnforceOnSnapshot(snapshot);
                if (mainForm.IsDisposed)
                    return;
                mainForm.BeginInvoke(async void () =>
                {
                    try
                    {
                        await enforcementRunner.ApplyEnforcementResult(result);
                        await enforcementRunner.ProcessExpiredContainersAtStartup();
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
                if (mainForm.IsDisposed)
                    return;
                mainForm.BeginInvoke(() => MessageBox.Show(
                    $"Startup enforcement failed:\n{ex.Message}\n\nACL rules may not be fully applied.",
                    "RunFence — Enforcement Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }
        });

        Task.WhenAll(stream1, stream2, stream3).ContinueWith(_ =>
            ShowBalloon(mainForm, "Some background startup tasks failed. Check logs for details."),
            TaskContinuationOptions.OnlyOnFaulted);

        if (startupOptions.PinBypassed && !startupOptions.IsBackground)
            ShowBalloon(mainForm, "RunFence started in tray. Click here to unlock.");
    }

    private static void ShowBalloon(MainForm mainForm, string text)
    {
        if (mainForm.IsDisposed)
            return;
        mainForm.BeginInvoke(() => mainForm.ShowTrayBalloon(text));
    }
}

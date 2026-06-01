using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

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
    ISessionSaver sessionSaver,
    IPackageInstallService packageInstallService,
    IStartupRepairWarningPresenter startupRepairWarningPresenter,
    StartupOptions startupOptions,
    ILoggingService log)
{
    /// <summary>
    /// Runs deferred startup work on the UI thread. Must be called via BeginInvoke after
    /// the main form handle is created, so that Task.Run streams can marshal back via BeginInvoke.
    /// </summary>
    public void Run(IDeferredStartupMainForm mainForm)
    {
        var preparation = PrepareStartupSnapshot();
        if (preparation.SaveFailureMessage != null)
        {
            startupRepairWarningPresenter.ShowStartupRepairWarning(preparation.SaveFailureMessage);
            return;
        }

        var snapshot = preparation.Snapshot!;
        var folderHandlerCleanupSidSnapshot = folderHandlerService.CaptureCleanupSidSnapshot();

        // Stream 1: fire-and-forget stale folder handler cleanup (registry enumeration/deletion)
        // and stale install script cleanup.
        var stream1 = Task.Run(() =>
        {
            folderHandlerService.CleanupStaleRegistrations(folderHandlerCleanupSidSnapshot);
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

        // Stream 3: enforcement chain - background I/O then UI-thread result application.
        var stream3 = Task.Run(() =>
        {
            try
            {
                var result = enforcementRunner.EnforceOnSnapshot(snapshot);
                if (mainForm.IsDisposed)
                    return;
                mainForm.BeginInvokeOnUiThread(async void () =>
                {
                    try
                    {
                        await enforcementRunner.ApplyEnforcementResult(result);
                        await enforcementRunner.ProcessExpiredContainersAtStartup();
                        await enforcementRunner.ProcessExpiredAccountsAsync();
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
            }
        });

        Task.WhenAll(stream1, stream2, stream3).ContinueWith(_ =>
            ShowBalloon(mainForm, "Some background startup tasks failed. Check logs for details."),
            TaskContinuationOptions.OnlyOnFaulted);

        if (startupOptions.PinBypassed && !startupOptions.IsBackground)
            ShowBalloon(mainForm, "RunFence started in tray. Click here to unlock.");
    }

    internal DeferredStartupPreparationResult PrepareStartupSnapshot()
    {
        // Pre-work on the UI thread before any background tasks:
        // refresh container SIDs, repair live AppEntry defaults, repair trusted missing paths, then snapshot.
        // All changes must happen before CreateSnapshot so the background work sees the repaired state.
        enforcementRunner.RefreshContainerSidsIfUserChanged();

        var defaultRepairResult = enforcementRunner.FixAppEntryDefaults();
        if (defaultRepairResult.Changed)
        {
            try
            {
                sessionSaver.SaveConfig();
            }
            catch (Exception ex)
            {
                log.Error("Deferred startup AppEntry repair save failed", ex);
                return new DeferredStartupPreparationResult(
                    Snapshot: null,
                    SaveFailureMessage: $"RunFence repaired invalid application defaults at startup, but saving those repairs failed:\n\n{ex.Message}");
            }
        }

        var pathRepairResult = enforcementRunner.RepairMissingAppEntryPaths();
        foreach (var warning in pathRepairResult.Warnings)
            log.Warn($"Startup app path repair warning: {warning}");

        if (pathRepairResult.SaveFailureMessage != null)
        {
            log.Error($"Deferred startup AppEntry path repair save failed: {pathRepairResult.SaveFailureMessage}");
            return new DeferredStartupPreparationResult(
                Snapshot: null,
                SaveFailureMessage: $"RunFence repaired missing application paths at startup, but saving those repairs failed:\n\n{pathRepairResult.SaveFailureMessage}");
        }

        return new DeferredStartupPreparationResult(
            Snapshot: sessionProvider.GetSession().Database.CreateSnapshot(),
            SaveFailureMessage: null);
    }

    private static void ShowBalloon(IDeferredStartupMainForm mainForm, string text)
    {
        if (mainForm.IsDisposed)
            return;
        mainForm.BeginInvokeOnUiThread(() => mainForm.ShowTrayBalloon(text));
    }
}

internal sealed record DeferredStartupPreparationResult(
    AppDatabase? Snapshot,
    string? SaveFailureMessage);

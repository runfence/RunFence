using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Account.Lifecycle;

/// <summary>
/// Manages ephemeral AppContainer cleanup on a 1h background timer.
/// Separate from EphemeralAccountService (different dependencies and deletion logic).
/// </summary>
public class EphemeralContainerService(
    IContainerDeletionService containerDeletion,
    IDatabaseService databaseService,
    ILoggingService log,
    ISessionProvider sessionProvider,
    IUiThreadInvoker uiThreadInvoker,
    IProcessListService processListService,
    ITrayBalloonService trayBalloonService)
    : IDisposable, IBackgroundService, IEphemeralContainerChangeSource
{
    private EphemeralTimerHelper? _timer;

    public event Action? ContainersChanged;

    public void Start()
    {
        log.Info("EphemeralContainerService: starting.");
        _timer = new EphemeralTimerHelper(uiThreadInvoker, ProcessExpiredContainers);
        _timer.Start();
        log.Info("EphemeralContainerService: started.");
    }

    public async Task ProcessExpiredContainers()
    {
        var session = sessionProvider.GetSession();
        var database = session.Database;
        var (orphaned, expired) = ClassifyEntries(database.AppContainers);
        var result = await ProcessEntries(orphaned, expired, containerDeletion, log, processListService);

        if (result.Changed)
        {
            databaseService.SaveConfig(database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
            ContainersChanged?.Invoke();
        }

        ShowWarnings(result.Warnings);
    }

    /// <summary>
    /// Processes expired ephemeral containers at startup before the timer service starts.
    /// Returns whether any changes were made plus warning-grade cleanup issues from completed work.
    /// </summary>
    public static async Task<EphemeralContainerProcessingResult> ProcessExpiredAtStartup(
        AppDatabase database,
        IContainerDeletionService containerDeletion, ILoggingService log,
        IProcessListService processListService)
    {
        var (orphaned, expired) = ClassifyEntries(database.AppContainers);

        log.Info($"EphemeralContainerService: processing expired containers at startup ({orphaned.Count} orphaned, {expired.Count} expired).");

        var result = await ProcessEntries(orphaned, expired, containerDeletion, log, processListService);

        log.Info("EphemeralContainerService: startup container processing complete.");
        return result;
    }

    /// <summary>
    /// Instance wrapper used from <see cref="AppLifecycleStarter"/>: processes expired containers
    /// at startup and saves the database via the injected <see cref="IDatabaseService"/> if any
    /// containers were removed.
    /// </summary>
    public async Task ProcessExpiredContainersAtStartup()
    {
        var session = sessionProvider.GetSession();
        var result = await ProcessExpiredAtStartup(session.Database, containerDeletion, log, processListService);
        if (result.Changed)
            databaseService.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);

        ShowWarnings(result.Warnings);
    }

    private static async Task<EphemeralContainerProcessingResult> ProcessEntries(
        List<AppContainerEntry> orphaned,
        List<AppContainerEntry> expired,
        IContainerDeletionService containerDeletion,
        ILoggingService log,
        IProcessListService processListService)
    {
        var warnings = new List<string>();
        bool changed = false;
        foreach (var entry in orphaned)
        {
            var containerSid = string.IsNullOrEmpty(entry.Sid) ? null : entry.Sid;
            var result = await containerDeletion.DeleteContainer(entry, containerSid);
            warnings.AddRange(result.Warnings);
            if (!result.Succeeded)
                continue; // preserve entry - skip DB cleanup so it can be retried next time
            changed = true;
        }

        var expiredSids = expired
            .Where(e => !string.IsNullOrEmpty(e.Sid))
            .Select(e => e.Sid)
            .ToList();
        var sidsWithProcesses = expiredSids.Count > 0
            ? processListService.GetSidsWithProcesses(expiredSids)
            : [];

        foreach (var entry in expired)
        {
            if (!string.IsNullOrEmpty(entry.Sid) && sidsWithProcesses.Contains(entry.Sid))
            {
                log.Info($"Postponing ephemeral container deletion for '{entry.Name}': processes still running under SID {entry.Sid}");
                entry.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
                changed = true;
                continue;
            }

            var containerSid = string.IsNullOrEmpty(entry.Sid) ? null : entry.Sid;
            var result = await containerDeletion.DeleteContainer(entry, containerSid);
            warnings.AddRange(result.Warnings);
            if (!result.Succeeded)
                continue;
            changed = true;
        }

        return EphemeralContainerProcessingResult.Create(changed, warnings);
    }

    private void ShowWarnings(IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            log.Warn($"EphemeralContainerService: {warning}");
            uiThreadInvoker.BeginInvoke(() => trayBalloonService.ShowWarning(warning));
        }
    }

    private static (List<AppContainerEntry> orphaned, List<AppContainerEntry> expired) ClassifyEntries(
        List<AppContainerEntry> containers)
    {
        var orphaned = new List<AppContainerEntry>();
        var expired = new List<AppContainerEntry>();

        foreach (var entry in containers)
        {
            if (!entry.IsEphemeral)
                continue;

            if (entry.DeleteAfterUtc == null)
            {
                orphaned.Add(entry);
            }
            else if (entry.DeleteAfterUtc <= DateTime.UtcNow)
            {
                expired.Add(entry);
            }
        }

        return (orphaned, expired);
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

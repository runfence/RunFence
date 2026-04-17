using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Startup;

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
    IProcessListService processListService)
    : IDisposable, IBackgroundService
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

    public void ProcessExpiredContainers()
    {
        var session = sessionProvider.GetSession();
        var database = session.Database;
        bool changed = false;

        var (orphaned, expired) = ClassifyEntries(database.AppContainers);
        changed |= ProcessOrphanedEntries(orphaned, containerDeletion);
        changed |= ProcessExpiredEntries(expired, containerDeletion, log, processListService);

        if (changed)
        {
            using var scope = session.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(database, scope.Data, session.CredentialStore.ArgonSalt);
            ContainersChanged?.Invoke();
        }
    }

    /// <summary>
    /// Processes expired ephemeral containers at startup before the timer service starts.
    /// Returns true if any changes were made.
    /// </summary>
    public static bool ProcessExpiredAtStartup(
        AppDatabase database,
        IContainerDeletionService containerDeletion, ILoggingService log,
        IProcessListService processListService)
    {
        bool changed = false;
        var (orphaned, expired) = ClassifyEntries(database.AppContainers);

        log.Info($"EphemeralContainerService: processing expired containers at startup ({orphaned.Count} orphaned, {expired.Count} expired).");

        changed |= ProcessOrphanedEntries(orphaned, containerDeletion);
        changed |= ProcessExpiredEntries(expired, containerDeletion, log, processListService);

        log.Info("EphemeralContainerService: startup container processing complete.");
        return changed;
    }

    /// <summary>
    /// Instance wrapper used from <see cref="AppLifecycleStarter"/>: processes expired containers
    /// at startup and saves the database via the injected <see cref="IDatabaseService"/> if any
    /// containers were removed.
    /// </summary>
    public void ProcessExpiredContainersAtStartup()
    {
        var session = sessionProvider.GetSession();
        if (ProcessExpiredAtStartup(session.Database, containerDeletion, log, processListService))
        {
            using var scope = session.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
        }
    }

    private static bool ProcessOrphanedEntries(List<AppContainerEntry> orphaned,
        IContainerDeletionService containerDeletion)
    {
        bool anyRemoved = false;
        foreach (var entry in orphaned)
        {
            if (!RunDeletion(entry, containerDeletion))
                continue; // preserve entry — skip DB cleanup so it can be retried next time
            anyRemoved = true;
        }

        return anyRemoved;
    }

    private static bool ProcessExpiredEntries(List<AppContainerEntry> expired,
        IContainerDeletionService containerDeletion, ILoggingService log,
        IProcessListService processListService)
    {
        bool changed = false;

        var expiredSids = expired
            .Where(e => !string.IsNullOrEmpty(e.Sid))
            .Select(e => e.Sid!)
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

            if (!RunDeletion(entry, containerDeletion))
                continue;
            changed = true;
        }

        return changed;
    }

    private static bool RunDeletion(AppContainerEntry entry, IContainerDeletionService containerDeletion)
    {
        var containerSid = string.IsNullOrEmpty(entry.Sid) ? null : entry.Sid;
        return containerDeletion.DeleteContainer(entry, containerSid);
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
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.Startup;

namespace RunFence.Account.Lifecycle;

/// <summary>
/// Manages ephemeral AppContainer cleanup on a 1h background timer.
/// Separate from EphemeralAccountService (different dependencies and deletion logic).
/// </summary>
public class EphemeralContainerService(
    IAppContainerService appContainerService,
    IContainerDeletionService containerDeletion,
    IDatabaseService databaseService,
    ILoggingService log,
    ISessionProvider sessionProvider,
    IUiThreadInvoker uiThreadInvoker)
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
        changed |= ProcessEntries(orphaned);

        foreach (var entry in expired)
        {
            if (!ProcessContainerCleanup(entry))
                continue;
            changed = true;
        }

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
        AppDatabase database, IAppContainerService appContainerService,
        IContainerDeletionService containerDeletion, ILoggingService log)
    {
        bool changed = false;
        var (orphaned, expired) = ClassifyEntries(database.AppContainers);

        log.Info($"EphemeralContainerService: processing expired containers at startup ({orphaned.Count} orphaned, {expired.Count} expired).");

        foreach (var entry in orphaned)
        {
            if (!RunDeletion(entry, appContainerService, containerDeletion, log))
                continue;
            changed = true;
        }

        foreach (var entry in expired)
        {
            if (!RunDeletion(entry, appContainerService, containerDeletion, log))
                continue;
            changed = true;
        }

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
        if (ProcessExpiredAtStartup(session.Database, appContainerService, containerDeletion, log))
        {
            using var scope = session.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
        }
    }

    private bool ProcessEntries(List<AppContainerEntry> entries)
    {
        bool anyRemoved = false;
        foreach (var entry in entries)
        {
            if (!ProcessContainerCleanup(entry))
                continue; // preserve entry — skip DB cleanup so it can be retried next time
            anyRemoved = true;
        }

        return anyRemoved;
    }

    private bool ProcessContainerCleanup(AppContainerEntry entry)
    {
        var containerSid = TryGetContainerSid(appContainerService, entry.Name, log);
        return containerDeletion.DeleteContainer(entry, containerSid);
    }

    private static bool RunDeletion(AppContainerEntry entry,
        IAppContainerService appContainerService, IContainerDeletionService containerDeletion,
        ILoggingService log)
    {
        var containerSid = TryGetContainerSid(appContainerService, entry.Name, log);
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

    private static string? TryGetContainerSid(IAppContainerService appContainerService, string containerName, ILoggingService log)
    {
        try
        {
            return appContainerService.GetSid(containerName);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to get SID for container '{containerName}': {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
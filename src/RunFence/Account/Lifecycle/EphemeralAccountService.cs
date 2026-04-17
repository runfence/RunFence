using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.Lifecycle;

public class EphemeralAccountService(
    IAccountLifecycleManager lifecycleManager,
    IAccountDeletionService accountDeletion,
    SessionPersistenceHelper persistenceHelper,
    ILocalUserProvider localUserProvider,
    ILoggingService log,
    IAccountValidationService accountValidation,
    ISessionProvider sessionProvider,
    IUiThreadInvoker uiThreadInvoker,
    ISidResolver sidResolver,
    IPathGrantService pathGrantService)
    : IDisposable, IBackgroundService
{
    private EphemeralTimerHelper? _timer;

    public event Action? AccountsChanged;

    public void Start()
    {
        log.Info("EphemeralAccountService: starting.");
        _timer = new EphemeralTimerHelper(uiThreadInvoker, ProcessExpiredAccounts);
        _timer.Start();
        log.Info("EphemeralAccountService: started.");
    }

    public void ProcessExpiredAccounts()
    {
        var session = sessionProvider.GetSession();
        var database = session.Database;
        var credentialStore = session.CredentialStore;

        bool changed = false;

        var ephemeralAccounts = database.Accounts.Where(a => a.DeleteAfterUtc.HasValue).ToList();
        var (orphaned, expired) = ClassifyEntries(ephemeralAccounts, credentialStore);
        log.Info($"EphemeralAccountService: processing expired accounts ({orphaned.Count} orphaned, {expired.Count} expired).");
        changed |= RemoveOrphanedEntries(orphaned, database);

        changed |= ProcessExpiredEntries(expired, database,
            logContext: null,
            deleteEntry: (entry, username) =>
            {
                try
                {
                    accountDeletion.DeleteAccount(entry.Sid, username, credentialStore);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to delete ephemeral account {entry.Sid}: {ex.Message}");
                    return false;
                }

                // Profile deletion is fire-and-forget: failure leaves an orphaned profile on disk,
                // which is detected and offered for cleanup during the next startup sequence.
                _ = lifecycleManager.DeleteProfileAsync(entry.Sid)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            log.Warn($"Failed to delete profile for ephemeral account {entry.Sid}: {(t.Exception!.InnerException ?? t.Exception).Message}. " +
                                      "The orphaned profile will be detected and offered for cleanup on next startup.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                return true;
            });

        if (changed)
        {
            persistenceHelper.SaveCredentialStoreAndConfig(credentialStore, database, session.PinDerivedKey);
            localUserProvider.InvalidateCache();
            AccountsChanged?.Invoke();
        }

        log.Info("EphemeralAccountService: expired account processing complete.");
    }

    /// <summary>
    /// Iterates expired entries, handling postpone (running processes) and skip (unknown username) cases.
    /// Calls <paramref name="deleteEntry"/> for each entry ready for deletion.
    /// Returns true if any changes were made.
    /// </summary>
    private bool ProcessExpiredEntries(
        List<AccountEntry> expired,
        AppDatabase database,
        string? logContext,
        Func<AccountEntry, string, bool> deleteEntry)
    {
        bool changed = false;
        var contextSuffix = logContext != null ? $" {logContext}" : "";

        foreach (var entry in expired)
        {
            var processes = accountValidation.GetProcessesRunningAsSid(entry.Sid);
            if (processes.Count > 0)
            {
                log.Info($"Postponing ephemeral account deletion for {entry.Sid}{contextSuffix}: {processes.Count} process(es) still running");
                entry.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
                changed = true;
                continue;
            }

            var username = SidNameResolver.ResolveUsername(entry.Sid, sidResolver, database.SidNames);
            if (username == null)
            {
                log.Info($"EphemeralAccountService: permanentizing expired entry for SID {entry.Sid} (username not resolvable); clearing grants to prevent stale state.");
                entry.DeleteAfterUtc = null;
                pathGrantService.RemoveAll(entry.Sid, updateFileSystem: false);
                database.RemoveAccountIfEmpty(entry.Sid);
                changed = true;
                continue;
            }

            if (deleteEntry(entry, username))
                changed = true;
        }

        return changed;
    }

    private bool RemoveOrphanedEntries(List<AccountEntry> orphaned, AppDatabase database)
    {
        foreach (var entry in orphaned)
        {
            log.Info($"EphemeralAccountService: permanentizing orphaned entry for SID {entry.Sid} (account not found on system and no credentials); clearing grants to prevent stale state.");
            entry.DeleteAfterUtc = null;
            pathGrantService.RemoveAll(entry.Sid, updateFileSystem: false);
            database.RemoveAccountIfEmpty(entry.Sid);
        }

        return orphaned.Count > 0;
    }

    private (List<AccountEntry> orphaned, List<AccountEntry> expired) ClassifyEntries(
        List<AccountEntry> ephemeralAccounts, CredentialStore credentialStore)
    {
        var orphaned = ephemeralAccounts
            .Where(e =>
            {
                if (sidResolver.TryResolveName(e.Sid) != null)
                    return false;
                return !credentialStore.Credentials.Any(c =>
                    string.Equals(c.Sid, e.Sid, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
        var orphanedSids = new HashSet<string>(orphaned.Select(e => e.Sid), StringComparer.OrdinalIgnoreCase);
        var expired = ephemeralAccounts
            .Where(e => !orphanedSids.Contains(e.Sid) && e.DeleteAfterUtc <= DateTime.UtcNow)
            .ToList();
        return (orphaned, expired);
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
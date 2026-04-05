using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.Lifecycle;

public class EphemeralAccountService : IDisposable, IBackgroundService
{
    private readonly IAccountLifecycleManager _lifecycleManager;
    private readonly IAccountDeletionService _accountDeletion;
    private readonly IAccountCredentialManager _credentialManager;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ILoggingService _log;
    private readonly IAccountValidationService _accountValidation;
    private readonly ISidResolver _sidResolver;
    private readonly ISessionProvider _sessionProvider;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private EphemeralTimerHelper? _timer;

    public event Action? AccountsChanged;

    public EphemeralAccountService(
        IAccountLifecycleManager lifecycleManager,
        IAccountDeletionService accountDeletion,
        IAccountCredentialManager credentialManager,
        ILocalUserProvider localUserProvider,
        ILoggingService log,
        IAccountValidationService accountValidation,
        ISessionProvider sessionProvider,
        IUiThreadInvoker uiThreadInvoker,
        ISidResolver sidResolver)
    {
        _lifecycleManager = lifecycleManager;
        _accountDeletion = accountDeletion;
        _credentialManager = credentialManager;
        _localUserProvider = localUserProvider;
        _log = log;
        _accountValidation = accountValidation;
        _sessionProvider = sessionProvider;
        _uiThreadInvoker = uiThreadInvoker;
        _sidResolver = sidResolver;
    }

    public void Start()
    {
        _log.Info("EphemeralAccountService: starting.");
        _timer = new EphemeralTimerHelper(_uiThreadInvoker, ProcessExpiredAccounts);
        _timer.Start();
        _log.Info("EphemeralAccountService: started.");
    }

    public void ProcessExpiredAccounts()
    {
        var session = _sessionProvider.GetSession();
        var database = session.Database;
        var credentialStore = session.CredentialStore;

        bool changed = false;

        var ephemeralAccounts = database.Accounts.Where(a => a.DeleteAfterUtc.HasValue).ToList();
        var (orphaned, expired) = ClassifyEntries(ephemeralAccounts, credentialStore);
        _log.Info($"EphemeralAccountService: processing expired accounts ({orphaned.Count} orphaned, {expired.Count} expired).");
        changed |= RemoveOrphanedEntries(orphaned, database);

        changed |= ProcessExpiredEntries(expired, database, _accountValidation, _sidResolver, _log,
            logContext: null,
            deleteEntry: (entry, username) =>
            {
                try
                {
                    _accountDeletion.DeleteAccount(entry.Sid, username, credentialStore);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Failed to delete ephemeral account {entry.Sid}: {ex.Message}");
                    return false;
                }

                _ = _lifecycleManager.DeleteProfileAsync(entry.Sid)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _log.Warn($"Failed to delete profile for ephemeral account {entry.Sid}: {(t.Exception!.InnerException ?? t.Exception).Message}. " +
                                      "The orphaned profile will be detected and offered for cleanup on next startup.");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                return true;
            });

        if (changed)
        {
            _credentialManager.SaveCredentialStoreAndConfig(credentialStore, database, session.PinDerivedKey);
            _localUserProvider.InvalidateCache();
            AccountsChanged?.Invoke();
        }

        _log.Info("EphemeralAccountService: expired account processing complete.");
    }

    /// <summary>
    /// Iterates expired entries, handling postpone (running processes) and skip (unknown username) cases.
    /// Calls <paramref name="deleteEntry"/> for each entry ready for deletion.
    /// Returns true if any changes were made.
    /// </summary>
    private static bool ProcessExpiredEntries(
        List<AccountEntry> expired,
        AppDatabase database,
        IAccountValidationService accountValidation,
        ISidResolver sidResolver,
        ILoggingService log,
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
                entry.DeleteAfterUtc = null;
                database.RemoveAccountIfEmpty(entry.Sid);
                changed = true;
                continue;
            }

            if (deleteEntry(entry, username))
                changed = true;
        }

        return changed;
    }

    private static bool RemoveOrphanedEntries(List<AccountEntry> orphaned, AppDatabase database)
    {
        foreach (var entry in orphaned)
        {
            entry.DeleteAfterUtc = null;
            database.RemoveAccountIfEmpty(entry.Sid);
        }

        return orphaned.Count > 0;
    }

    private (List<AccountEntry> orphaned, List<AccountEntry> expired) ClassifyEntries(
        List<AccountEntry> ephemeralAccounts, CredentialStore credentialStore) =>
        ClassifyEntriesStatic(ephemeralAccounts, credentialStore, _sidResolver);

    private static (List<AccountEntry> orphaned, List<AccountEntry> expired) ClassifyEntriesStatic(
        List<AccountEntry> ephemeralAccounts, CredentialStore credentialStore, ISidResolver sidResolver)
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
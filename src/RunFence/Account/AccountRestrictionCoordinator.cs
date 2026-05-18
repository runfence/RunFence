namespace RunFence.Account;

public class AccountRestrictionCoordinator(
    IAccountToggleService accountToggleService,
    IAccountLifecycleManager lifecycleManager,
    IAccountLoginRestrictionService loginRestrictionService,
    IAccountLsaRestrictionService lsaRestrictionService,
    IGroupPolicyScriptHelper groupPolicyScriptHelper) : IAccountRestrictionCoordinator
{
    private static readonly AccountRestrictionKind[] ResultOrder =
    [
        AccountRestrictionKind.HideLogon,
        AccountRestrictionKind.LogonScript,
        AccountRestrictionKind.NetworkLogin,
        AccountRestrictionKind.BackgroundAutorun,
        AccountRestrictionKind.LsaPolicy
    ];

    public AccountRestrictionResult ApplyRestrictions(string sid, string username, bool logonBlocked, bool networkLoginBlocked, bool backgroundAutorunBlocked)
    {
        var entries = new Dictionary<AccountRestrictionKind, AccountRestrictionEntry>();

        var logon = accountToggleService.SetLogonBlocked(sid, username, logonBlocked);
        if (!logon.Success)
        {
            UpdateEntries(entries, [AccountRestrictionKind.HideLogon, AccountRestrictionKind.LogonScript], logon.FailureStatus,
                rollbackAttempted: logon.RollbackAttempted,
                error: logon.ErrorMessage);
        }
        else
        {
            UpdateEntries(entries, [AccountRestrictionKind.HideLogon, AccountRestrictionKind.LogonScript], AccountRestrictionStatus.Succeeded,
                rollbackAttempted: false,
                error: null);
        }

        try
        {
            lsaRestrictionService.SetLocalOnlyBySid(sid, networkLoginBlocked);
            UpdateEntries(entries, [AccountRestrictionKind.NetworkLogin], AccountRestrictionStatus.Succeeded,
                rollbackAttempted: false,
                error: null);
        }
        catch (Exception ex)
        {
            UpdateEntries(entries, [AccountRestrictionKind.NetworkLogin], GetFailureStatus(ex),
                rollbackAttempted: GetRollbackAttempted(ex),
                error: ex.Message);
        }

        try
        {
            lsaRestrictionService.SetNoBgAutostartBySid(sid, backgroundAutorunBlocked);
            UpdateEntries(entries, [AccountRestrictionKind.BackgroundAutorun], AccountRestrictionStatus.Succeeded,
                rollbackAttempted: false,
                error: null);
        }
        catch (Exception ex)
        {
            UpdateEntries(entries, [AccountRestrictionKind.BackgroundAutorun], GetFailureStatus(ex),
                rollbackAttempted: GetRollbackAttempted(ex),
                error: ex.Message);
        }

        UpdateLsaPolicyEntry(entries);
        return new(BuildEntries(entries));
    }

    public AccountRestrictionResult RevertRestrictions(string sid, string username)
    {
        return ApplyRestrictions(sid, username, logonBlocked: false, networkLoginBlocked: false, backgroundAutorunBlocked: false);
    }

    public AccountRestrictionResult MigrateRestrictions(string sourceSid, string sourceUsername, string targetSid, string targetUsername)
    {
        var sourceSnapshot = CaptureSnapshot(sourceSid, sourceUsername);

        var applyTarget = ApplyRestrictions(
            targetSid,
            targetUsername,
            sourceSnapshot.NoLogonBlockedFailClosed,
            sourceSnapshot.NetworkLoginBlockedFailClosed,
            sourceSnapshot.BackgroundAutorunBlockedFailClosed);
        if (applyTarget.Entries.Any(e => e.Status != AccountRestrictionStatus.Succeeded))
            return applyTarget;

        var removeSource = ApplyRestrictions(sourceSid, sourceUsername, false, false, false);
        return new(applyTarget.Entries.Concat(removeSource.Entries).ToList());
    }

    public AccountRestrictionResult DeleteRestrictions(string sid, string username)
    {
        lifecycleManager.ClearAccountRestrictions(sid, username);
        return new(
        [
            new(AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, false, null),
            new(AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, false, null),
            new(AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, false, null),
            new(AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Succeeded, false, null),
            new(AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Succeeded, false, null)
        ]);
    }

    private static IReadOnlyList<AccountRestrictionEntry> BuildEntries(
        IReadOnlyDictionary<AccountRestrictionKind, AccountRestrictionEntry> entries)
        => ResultOrder.Select(kind => entries[kind]).ToList();

    private static void UpdateEntries(
        Dictionary<AccountRestrictionKind, AccountRestrictionEntry> entries,
        IReadOnlyList<AccountRestrictionKind> kinds,
        AccountRestrictionStatus status,
        bool rollbackAttempted,
        string? error)
    {
        foreach (var kind in kinds)
        {
            entries[kind] = new AccountRestrictionEntry(
                kind,
                status,
                rollbackAttempted,
                error);
        }
    }

    private static void UpdateLsaPolicyEntry(Dictionary<AccountRestrictionKind, AccountRestrictionEntry> entries)
    {
        var networkEntry = entries[AccountRestrictionKind.NetworkLogin];
        var backgroundEntry = entries[AccountRestrictionKind.BackgroundAutorun];
        var rollbackAttempted = networkEntry.RollbackAttempted || backgroundEntry.RollbackAttempted;
        var error = CombineEntryErrors(
            (AccountRestrictionKind.NetworkLogin, networkEntry),
            (AccountRestrictionKind.BackgroundAutorun, backgroundEntry));

        if (networkEntry.Status == AccountRestrictionStatus.Failed ||
            backgroundEntry.Status == AccountRestrictionStatus.Failed)
        {
            entries[AccountRestrictionKind.LsaPolicy] = new AccountRestrictionEntry(
                AccountRestrictionKind.LsaPolicy,
                AccountRestrictionStatus.Failed,
                rollbackAttempted,
                error);
            return;
        }

        if (networkEntry.Status == AccountRestrictionStatus.RolledBack ||
            backgroundEntry.Status == AccountRestrictionStatus.RolledBack)
        {
            entries[AccountRestrictionKind.LsaPolicy] = new AccountRestrictionEntry(
                AccountRestrictionKind.LsaPolicy,
                AccountRestrictionStatus.RolledBack,
                rollbackAttempted,
                Error: null);
            return;
        }

        entries[AccountRestrictionKind.LsaPolicy] = new AccountRestrictionEntry(
            AccountRestrictionKind.LsaPolicy,
            AccountRestrictionStatus.Succeeded,
            rollbackAttempted,
            Error: null);
    }

    private AccountRestrictionSnapshot CaptureSnapshot(string sid, string username) =>
        new(
            groupPolicyScriptHelper.IsLoginBlocked(sid),
            loginRestrictionService.IsAccountHidden(username),
            lsaRestrictionService.CaptureSnapshot(sid));

    private static string? CombineEntryErrors(params (AccountRestrictionKind Kind, AccountRestrictionEntry Entry)[] entries)
    {
        var errors = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Entry.Error))
            .Select(x => $"{x.Kind}: {x.Entry.Error}")
            .ToList();
        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    private static AccountRestrictionStatus GetFailureStatus(Exception ex)
        => ex is AccountRestrictionOperationException restrictionException
            ? restrictionException.Status
            : AccountRestrictionStatus.Failed;

    private static bool GetRollbackAttempted(Exception ex)
        => ex is AccountRestrictionOperationException restrictionException &&
           restrictionException.RollbackAttempted;
}

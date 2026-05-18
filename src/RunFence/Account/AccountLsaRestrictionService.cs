using RunFence.Core;

namespace RunFence.Account;

public class AccountLsaRestrictionService(
    ILoggingService log,
    IAccountValidationService accountValidation,
    ILsaRightsHelper lsaRights) : IAccountLsaRestrictionService
{
    private static readonly string[] LocalOnlyRights =
        [LsaRightsHelper.SeDenyNetworkLogonRight, LsaRightsHelper.SeDenyRemoteInteractiveLogonRight];

    private static readonly string[] NoBgAutostartRights =
        [LsaRightsHelper.SeDenyBatchLogonRight, LsaRightsHelper.SeDenyServiceLogonRight];

    public void SetLocalOnlyBySid(string sid, bool localOnly)
    {
        if (localOnly)
            accountValidation.ValidateNotLastAdmin(sid, "set local-only for");

        var snapshot = CaptureSnapshot(sid);
        SetRights(sid, LocalOnlyRights, localOnly, "local-only",
            () => RestoreLocalOnlyState(sid, snapshot),
            "Set local-only (deny network + RDP)", "Removed local-only restrictions");
    }

    public bool? GetLocalOnlyState(string sid)
        => GetRightsState(sid, snapshot => snapshot.NetworkLoginState, "local-only");

    public void SetNoBgAutostartBySid(string sid, bool blocked)
    {
        var snapshot = CaptureSnapshot(sid);
        SetRights(sid, NoBgAutostartRights, blocked, "no-bg-autostart",
            () => RestoreNoBgAutostartState(sid, snapshot),
            "Set no-bg-autostart (deny batch + service logon)", "Removed no-bg-autostart restrictions");
    }

    public bool? GetNoBgAutostartState(string sid)
        => GetRightsState(sid, snapshot => snapshot.BackgroundAutorunState, "no-bg-autostart");

    public AccountLsaRestrictionSnapshot CaptureSnapshot(string sid)
    {
        try
        {
            return ReadSnapshot(sid);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to capture LSA restriction snapshot for {sid}", ex);
            throw;
        }
    }

    public void RestoreLocalOnlyState(string sid, AccountLsaRestrictionSnapshot snapshot)
    {
        RestoreRights(
            sid,
            snapshot,
            [(LsaRightsHelper.SeDenyNetworkLogonRight, snapshot.DenyNetworkLogon),
             (LsaRightsHelper.SeDenyRemoteInteractiveLogonRight, snapshot.DenyRemoteInteractiveLogon)],
            "local-only");
    }

    public void RestoreNoBgAutostartState(string sid, AccountLsaRestrictionSnapshot snapshot)
    {
        RestoreRights(
            sid,
            snapshot,
            [(LsaRightsHelper.SeDenyBatchLogonRight, snapshot.DenyBatchLogon),
             (LsaRightsHelper.SeDenyServiceLogonRight, snapshot.DenyServiceLogon)],
            "no-bg-autostart");
    }

    private void SetRights(string sid, string[] rightNames, bool enable, string featureName, Action rollback,
        string enabledLogMessage, string disabledLogMessage)
    {
        try
        {
            var sidBytes = lsaRights.GetSidBytes(sid);
            if (enable)
            {
                lsaRights.AddAccountRights(sidBytes, rightNames);
                log.Info($"{enabledLogMessage} for: {sid}");
            }
            else
            {
                lsaRights.RemoveAccountRights(sidBytes, rightNames);
                log.Info($"{disabledLogMessage} for: {sid}");
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set {featureName} status for {sid}", ex);
            try
            {
                rollback();
            }
            catch (Exception rollbackEx)
            {
                log.Error($"Failed to roll back {featureName} status for {sid}", rollbackEx);
                throw new AccountRestrictionOperationException(
                    $"{ex.Message} Rollback failed: {rollbackEx.Message}",
                    AccountRestrictionStatus.Failed,
                    rollbackAttempted: true,
                    ex);
            }

            throw new AccountRestrictionOperationException(
                ex.Message,
                AccountRestrictionStatus.RolledBack,
                rollbackAttempted: true,
                ex);
        }
    }

    private bool? GetRightsState(string sid, Func<AccountLsaRestrictionSnapshot, bool?> selector, string featureName)
    {
        try
        {
            return selector(ReadSnapshot(sid));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check {featureName} state for {sid}", ex);
            return false;
        }
    }

    private AccountLsaRestrictionSnapshot ReadSnapshot(string sid)
    {
        var sidBytes = lsaRights.GetSidBytes(sid);
        var rights = lsaRights.EnumerateAccountRights(sidBytes);
        return new AccountLsaRestrictionSnapshot(
            DenyNetworkLogon: rights.Contains(LsaRightsHelper.SeDenyNetworkLogonRight),
            DenyRemoteInteractiveLogon: rights.Contains(LsaRightsHelper.SeDenyRemoteInteractiveLogonRight),
            DenyBatchLogon: rights.Contains(LsaRightsHelper.SeDenyBatchLogonRight),
            DenyServiceLogon: rights.Contains(LsaRightsHelper.SeDenyServiceLogonRight));
    }

    private void RestoreRights(
        string sid,
        AccountLsaRestrictionSnapshot snapshot,
        IReadOnlyList<(string RightName, bool ShouldExist)> rights,
        string featureName)
    {
        try
        {
            var sidBytes = lsaRights.GetSidBytes(sid);
            var currentRights = lsaRights.EnumerateAccountRights(sidBytes).ToHashSet(StringComparer.Ordinal);
            foreach (var (rightName, shouldExist) in rights)
                SyncRight(sidBytes, currentRights, rightName, shouldExist);

            log.Info($"Restored exact {featureName} restriction state for: {sid}");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to restore {featureName} restriction snapshot for {sid}", ex);
            throw;
        }
    }

    private void SyncRight(byte[] sidBytes, ISet<string> currentRights, string rightName, bool shouldExist)
    {
        var exists = currentRights.Contains(rightName);
        if (shouldExist == exists)
            return;

        if (shouldExist)
        {
            lsaRights.AddAccountRights(sidBytes, [rightName]);
            currentRights.Add(rightName);
            return;
        }

        lsaRights.RemoveAccountRights(sidBytes, [rightName]);
        currentRights.Remove(rightName);
    }
}

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

    public bool IsLocalOnlyBySid(string sid)
        => HasAllRights(sid, LocalOnlyRights, "local-only");

    public void SetLocalOnlyBySid(string sid, bool localOnly)
    {
        if (localOnly)
            accountValidation.ValidateNotLastAdmin(sid, "set local-only for");

        SetRights(sid, LocalOnlyRights, localOnly, "local-only",
            "Set local-only (deny network + RDP)", "Removed local-only restrictions");
    }

    public bool? GetLocalOnlyState(string sid)
        => GetRightsState(sid, LocalOnlyRights, "local-only");

    public bool IsNoBgAutostartBySid(string sid)
        => HasAllRights(sid, NoBgAutostartRights, "no-bg-autostart");

    public void SetNoBgAutostartBySid(string sid, bool blocked)
        => SetRights(sid, NoBgAutostartRights, blocked, "no-bg-autostart",
            "Set no-bg-autostart (deny batch + service logon)", "Removed no-bg-autostart restrictions");

    public bool? GetNoBgAutostartState(string sid)
        => GetRightsState(sid, NoBgAutostartRights, "no-bg-autostart");

    private bool HasAllRights(string sid, string[] rightNames, string featureName)
    {
        try
        {
            var sidBytes = lsaRights.GetSidBytes(sid);
            var rights = lsaRights.EnumerateAccountRights(sidBytes);
            return rightNames.All(rights.Contains);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check {featureName} status for {sid}", ex);
            return false;
        }
    }

    private void SetRights(string sid, string[] rightNames, bool enable, string featureName,
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
            throw;
        }
    }

    private bool? GetRightsState(string sid, string[] rightNames, string featureName)
    {
        try
        {
            var sidBytes = lsaRights.GetSidBytes(sid);
            var rights = lsaRights.EnumerateAccountRights(sidBytes);
            int count = rightNames.Count(rights.Contains);
            if (count == rightNames.Length)
                return true;
            if (count == 0)
                return false;
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to check {featureName} state for {sid}", ex);
            return false;
        }
    }
}
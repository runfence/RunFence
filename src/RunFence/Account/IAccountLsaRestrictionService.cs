namespace RunFence.Account;

public interface IAccountLsaRestrictionService
{
    void SetLocalOnlyBySid(string sid, bool localOnly);

    /// <summary>Returns true=both rights, false=neither, null=partial.</summary>
    bool? GetLocalOnlyState(string sid);

    void SetNoBgAutostartBySid(string sid, bool blocked);

    /// <summary>Returns true=both rights, false=neither, null=partial.</summary>
    bool? GetNoBgAutostartState(string sid);

    /// <summary>
    /// Captures the exact managed LSA deny-right state for network logon and background autorun.
    /// Unlike the bool? helpers, this preserves partial states exactly for migration and rollback.
    /// Throws if the state cannot be read.
    /// </summary>
    AccountLsaRestrictionSnapshot CaptureSnapshot(string sid);

    void RestoreLocalOnlyState(string sid, AccountLsaRestrictionSnapshot snapshot);

    void RestoreNoBgAutostartState(string sid, AccountLsaRestrictionSnapshot snapshot);
}

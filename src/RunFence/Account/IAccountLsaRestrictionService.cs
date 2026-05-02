namespace RunFence.Account;

public interface IAccountLsaRestrictionService
{
    void SetLocalOnlyBySid(string sid, bool localOnly);

    /// <summary>Returns true=both rights, false=neither, null=partial.</summary>
    bool? GetLocalOnlyState(string sid);

    void SetNoBgAutostartBySid(string sid, bool blocked);

    /// <summary>Returns true=both rights, false=neither, null=partial.</summary>
    bool? GetNoBgAutostartState(string sid);
}
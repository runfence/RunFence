namespace RunFence.Account;

public class AccountRestrictionHelper(
    IAccountLoginRestrictionService login,
    IAccountLsaRestrictionService lsa) : IAccountRestrictionService
{
    public bool IsAccountHidden(string username) => login.IsAccountHidden(username);
    public void SetAccountHidden(string username, string sid, bool hidden) => login.SetAccountHidden(username, sid, hidden);
    public bool IsLoginBlockedBySid(string sid) => login.IsLoginBlockedBySid(sid);
    public SetLoginBlockedResult SetLoginBlockedBySid(string sid, string username, bool blocked) => login.SetLoginBlockedBySid(sid, username, blocked);
    public bool? GetNoLogonState(string sid, string? username) => login.GetNoLogonState(sid, username);
    public void SetUacAdminEnumeration(bool enumerate) => login.SetUacAdminEnumeration(enumerate);
    public bool IsLocalOnlyBySid(string sid) => lsa.IsLocalOnlyBySid(sid);
    public void SetLocalOnlyBySid(string sid, bool localOnly) => lsa.SetLocalOnlyBySid(sid, localOnly);
    public bool? GetLocalOnlyState(string sid) => lsa.GetLocalOnlyState(sid);
    public bool IsNoBgAutostartBySid(string sid) => lsa.IsNoBgAutostartBySid(sid);
    public void SetNoBgAutostartBySid(string sid, bool blocked) => lsa.SetNoBgAutostartBySid(sid, blocked);
    public bool? GetNoBgAutostartState(string sid) => lsa.GetNoBgAutostartState(sid);
}
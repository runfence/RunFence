namespace RunFence.Account;

public interface IAccountRestrictionCoordinator
{
    AccountRestrictionResult ApplyRestrictions(string sid, string username, bool logonBlocked, bool networkLoginBlocked, bool backgroundAutorunBlocked);
    AccountRestrictionResult RevertRestrictions(string sid, string username);
    AccountRestrictionResult MigrateRestrictions(string sourceSid, string sourceUsername, string targetSid, string targetUsername);
    AccountRestrictionResult DeleteRestrictions(string sid, string username);
}

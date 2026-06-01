namespace RunFence.Acl;

public interface IGrantAccountCleanupService
{
    GrantApplyResult RemoveAll(string accountSid);

    GrantApplyResult UntrackAll(string accountSid);
}

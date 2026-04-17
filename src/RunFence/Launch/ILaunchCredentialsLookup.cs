namespace RunFence.Launch;

public interface ILaunchCredentialsLookup
{
    LaunchCredentials GetBySid(string accountSid);
}

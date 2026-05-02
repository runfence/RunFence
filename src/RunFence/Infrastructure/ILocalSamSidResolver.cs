namespace RunFence.Infrastructure;

public interface ILocalSamSidResolver
{
    bool TryGetLocalUserSid(string username, out string sid);
    string GetRequiredLocalUserSid(string username);
}

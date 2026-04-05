using RunFence.Core;

namespace RunFence.Acl.Permissions;

public interface IInteractiveUserResolver
{
    string? GetInteractiveUserSid();
}

public class DefaultInteractiveUserResolver : IInteractiveUserResolver
{
    public string? GetInteractiveUserSid() => SidResolutionHelper.GetInteractiveUserSid();
}
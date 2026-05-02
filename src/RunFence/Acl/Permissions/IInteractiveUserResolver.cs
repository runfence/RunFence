using RunFence.Infrastructure;

namespace RunFence.Acl.Permissions;

public interface IInteractiveUserResolver
{
    string? GetInteractiveUserSid();
}

public class DefaultInteractiveUserResolver(IInteractiveUserSidResolver interactiveUserSidResolver) : IInteractiveUserResolver
{
    public string? GetInteractiveUserSid() => interactiveUserSidResolver.GetInteractiveUserSid();
}

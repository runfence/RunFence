using System.Security.Principal;

namespace RunFence.AppxLauncher;

public sealed class WindowsIdentityAppxCurrentUserSidProvider : IAppxCurrentUserSidProvider
{
    public string? GetCurrentUserSid() => WindowsIdentity.GetCurrent().User?.Value;
}

using System.Security.Principal;

namespace RunFence.Ipc;

public class CurrentProcessSidProvider : ICurrentProcessSidProvider
{
    private readonly SecurityIdentifier? _sid = WindowsIdentity.GetCurrent().User;

    public SecurityIdentifier? GetCurrentProcessSid() => _sid;
}

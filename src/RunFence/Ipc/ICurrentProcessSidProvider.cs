using System.Security.Principal;

namespace RunFence.Ipc;

public interface ICurrentProcessSidProvider
{
    SecurityIdentifier? GetCurrentProcessSid();
}

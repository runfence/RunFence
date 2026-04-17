using System.IO.Pipes;

namespace RunFence.Ipc;

public interface IIpcIdentityExtractor
{
    /// <summary>
    /// Extracts caller context via pipe impersonation. Always returns non-null.
    /// On impersonation failure, returns context with IdentityFromImpersonation = false and null SID/identity.
    /// </summary>
    IpcCallerContext Extract(NamedPipeServerStream pipe);
}

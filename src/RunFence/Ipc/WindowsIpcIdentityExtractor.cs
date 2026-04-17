using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Ipc;

public class WindowsIpcIdentityExtractor(ILoggingService log) : IIpcIdentityExtractor
{
    public IpcCallerContext Extract(NamedPipeServerStream pipe)
    {
        string? callerIdentity = null;
        string? callerSid = null;
        bool isAdmin = false;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var clientIdentity = WindowsIdentity.GetCurrent();
                callerIdentity = clientIdentity.Name;
                callerSid = clientIdentity.User?.Value;
                var principal = new WindowsPrincipal(clientIdentity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            });
            // Identity obtained via RunAsClient pipe impersonation — tamper-evident SID source.
            // Set even if callerSid is null (e.g. system account with no user SID) — impersonation itself succeeded.
            return new IpcCallerContext(callerIdentity, callerSid, isAdmin, IdentityFromImpersonation: true);
        }
        catch (Exception ex)
        {
            log.Error("Failed to identify IPC caller", ex);
            return new IpcCallerContext(null, null, false, IdentityFromImpersonation: false);
        }
    }
}

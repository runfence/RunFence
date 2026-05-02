using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed class JobKeeperPipeServerFactory(
    IKernelObjectMandatoryLabelService mandatoryLabelService) : IJobKeeperPipeServerFactory
{
    public NamedPipeServerStream Create(JobKeeperInstanceIdentity identity, SecurityIdentifier targetUserSid)
    {
        var pipeSecurity = new PipeSecurity();
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(targetUserSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));

        var pipe = NamedPipeServerStreamAcl.Create(
            identity.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 65536,
            outBufferSize: 65536,
            pipeSecurity);

        if (identity.ExpectedMode == JobKeeperIntegrityMode.LowIntegrity)
        {
            try { mandatoryLabelService.ApplyLowIntegrityLabel(pipe.SafePipeHandle.DangerousGetHandle()); }
            catch { pipe.Dispose(); throw; }
        }

        return pipe;
    }
}

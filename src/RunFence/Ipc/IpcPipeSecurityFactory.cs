using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Ipc;

public class IpcPipeSecurityFactory(ICurrentProcessSidProvider currentProcessSidProvider)
{
    public PipeSecurity Create()
    {
        var pipeSecurity = new PipeSecurity();

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            adminSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            networkSid,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        var serverSid = currentProcessSidProvider.GetCurrentProcessSid();
        if (serverSid != null)
        {
            const PipeAccessRights serverInstanceRights =
                PipeAccessRights.ReadWrite
                | PipeAccessRights.CreateNewInstance
                | PipeAccessRights.ChangePermissions
                | PipeAccessRights.Synchronize;
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                serverSid,
                serverInstanceRights,
                AccessControlType.Allow));
        }

        var callerRights = PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize;

        var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            worldSid,
            callerRights,
            AccessControlType.Allow));

        var restrictedSid = new SecurityIdentifier(WellKnownSidType.RestrictedCodeSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            restrictedSid,
            callerRights,
            AccessControlType.Allow));

        return pipeSecurity;
    }
}

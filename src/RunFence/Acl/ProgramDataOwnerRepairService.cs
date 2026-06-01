using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl;

public class ProgramDataOwnerRepairService(
    ILoggingService log,
    IHandleSecurityDescriptorAccessor aclAccessor,
    ProgramDataOwnerPolicyService ownerPolicyService)
{
    public bool RepairOwner(
        SafeFileHandle handle,
        string path,
        bool isDirectory,
        ProgramDataAllowedOwnerPolicy ownerPolicy,
        IReadOnlyCollection<SecurityIdentifier>? expectedAdditionalOwners = null)
    {
        var currentOwnerSid = GetOwnerSid(aclAccessor.GetSecurity(handle, isDirectory));
        var changed = !ownerPolicyService.IsAllowedOwner(currentOwnerSid, ownerPolicy, expectedAdditionalOwners);
        if (changed)
        {
            var preferredOwnerSid = ownerPolicyService.GetPreferredRepairOwnerSid();
            aclAccessor.SetOwnerWithFallback(handle, preferredOwnerSid);
            log.Info(
                $"ProgramData security updated owner on '{path}' from '{currentOwnerSid.Value}' to '{preferredOwnerSid.Value}'.");
        }

        var repairedOwnerSid = GetOwnerSid(aclAccessor.GetSecurity(handle, isDirectory));
        if (!ownerPolicyService.IsAllowedOwner(repairedOwnerSid, ownerPolicy, expectedAdditionalOwners))
        {
            throw new InvalidOperationException($"Managed ProgramData object owner '{repairedOwnerSid.Value}' is not allowed after repair.");
        }

        return changed;
    }

    private static SecurityIdentifier GetOwnerSid(FileSystemSecurity security)
        => (SecurityIdentifier?)security.GetOwner(typeof(SecurityIdentifier))
           ?? throw new InvalidOperationException("Managed ProgramData object does not have a SID owner.");
}

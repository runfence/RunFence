using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Acl;

public class ProgramDataOwnerPolicyService(ProgramDataPathPolicyCatalog pathPolicyCatalog)
{
    public SecurityIdentifier GetPreferredRepairOwnerSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User
            ?? AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks()
            ?? new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
    }

    public ProgramDataAllowedOwnerPolicy GetOwnerPolicy(string normalizedPath)
        => pathPolicyCatalog.ResolveOwnerPolicy(normalizedPath);

    public bool IsAllowedOwner(
        SecurityIdentifier ownerSid,
        ProgramDataAllowedOwnerPolicy ownerPolicy,
        IReadOnlyCollection<SecurityIdentifier>? expectedAdditionalOwners = null)
    {
        if (ownerSid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
        {
            return true;
        }

        if (ownerSid.Equals(GetPreferredRepairOwnerSid()))
        {
            return true;
        }

        if (ownerPolicy != ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners ||
            expectedAdditionalOwners == null)
        {
            return false;
        }

        return expectedAdditionalOwners.Any(expected => expected.Equals(ownerSid));
    }
}

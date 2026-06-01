using System.Security.Principal;

namespace RunFence.Infrastructure;

public static class RestrictedJobSecurityValidator
{
    private const int GenericAllAccessMask = 0x10000000;
    private const int GenericReadAccessMask = unchecked((int)0x80000000);
    private const int GenericWriteAccessMask = 0x40000000;
    private const int GenericExecuteAccessMask = 0x20000000;
    private const int DeleteAccessMask = 0x00010000;
    private const int WriteDacAccessMask = 0x00040000;
    private const int WriteOwnerAccessMask = 0x00080000;
    private const int JobObjectAssignProcessAccessMask = 0x0001;
    private const int JobObjectSetAttributesAccessMask = 0x0002;
    private const int JobObjectTerminateAccessMask = 0x0008;
    private const int JobObjectAllAccessMask = 0x001F001F;
    private const int HarmlessReadAccessMask = (int)(
        FileSecurityNative.READ_CONTROL
        | KernelObjectAccessRights.Synchronize
        | ProcessJobManager.JobObjectQuery);
    private const int HarmlessExecuteAccessMask = (int)(
        FileSecurityNative.READ_CONTROL
        | KernelObjectAccessRights.Synchronize);
    private const int DangerousAccessMask =
        DeleteAccessMask
        | WriteDacAccessMask
        | WriteOwnerAccessMask
        | JobObjectAssignProcessAccessMask
        | JobObjectSetAttributesAccessMask
        | JobObjectTerminateAccessMask;

    private static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private static readonly SecurityIdentifier LocalSystemSid =
        new(WellKnownSidType.LocalSystemSid, null);

    public static string? GetSecurityFailure(JobObjectSecuritySnapshot? security)
    {
        if (security?.Owner == null)
            return "job security descriptor or owner could not be read.";

        if (!security.HasDiscretionaryAcl)
            return "job DACL is missing.";

        if (!IsTrustedOwner(security.Owner))
            return $"job owner mismatch: expected {AdministratorsSid.Value} or {LocalSystemSid.Value}, actual {security.Owner.Value}.";

        if (TryIsPolicyMutableByUntrustedPrincipal(security))
        {
            var failingEntry = security.AccessEntries
                .Where(entry => entry.IsAllow && !IsTrustedPrincipal(entry.Identity))
                .First(CanModifyRestrictions);
            return $"job DACL grants dangerous access 0x{failingEntry.AccessMask:X} to {failingEntry.Identity.Value}.";
        }

        return null;
    }

    public static bool TryIsPolicyMutableByUntrustedPrincipal(JobObjectSecuritySnapshot snapshot) =>
        snapshot.AccessEntries
            .Where(entry => entry.IsAllow && !IsTrustedPrincipal(entry.Identity))
            .Any(CanModifyRestrictions);

    public static bool CanModifyRestrictions(JobObjectAccessEntry ace)
    {
        var normalizedAccessMask = MapGenericAccessMask(ace.AccessMask);
        return (normalizedAccessMask & DangerousAccessMask) != 0
            || (normalizedAccessMask & JobObjectAllAccessMask) == JobObjectAllAccessMask;
    }

    public static int MapGenericAccessMask(int mask)
    {
        var mapped = mask & ~(GenericReadAccessMask | GenericWriteAccessMask | GenericExecuteAccessMask | GenericAllAccessMask);

        if ((mask & GenericReadAccessMask) != 0)
            mapped |= HarmlessReadAccessMask;

        if ((mask & GenericWriteAccessMask) != 0)
        {
            mapped |= (int)(FileSecurityNative.READ_CONTROL | KernelObjectAccessRights.Synchronize);
            mapped |= JobObjectAssignProcessAccessMask | JobObjectSetAttributesAccessMask;
        }

        if ((mask & GenericExecuteAccessMask) != 0)
            mapped |= HarmlessExecuteAccessMask;

        if ((mask & GenericAllAccessMask) != 0)
            mapped |= JobObjectAllAccessMask;

        return mapped;
    }

    public static bool IsTrustedOwner(SecurityIdentifier owner) => IsTrustedPrincipal(owner);

    private static bool IsTrustedPrincipal(SecurityIdentifier identity) =>
        AdministratorsSid.Equals(identity) || LocalSystemSid.Equals(identity);
}

using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

public class ProgramDataManagedObjectRepairer(
    IProgramDataPathGuard pathGuard,
    IHandleSecurityDescriptorAccessor handleSecurityDescriptorAccessor,
    ProgramDataOwnerPolicyService ownerPolicyService,
    ProgramDataOwnerRepairService ownerRepairService,
    ProgramDataSecurityApplier securityApplier,
    ProgramDataSecurityVerifier securityVerifier)
    : IProgramDataManagedObjectRepairService
{
    public ProgramDataSecurityRepairResult EnsureManagedFileSecurity(string filePath, ProgramDataFileAclProfile aclProfile)
    {
        var normalizedPath = pathGuard.NormalizeExistingPathUnderRoot(filePath, ProgramDataObjectKind.File);
        var ownerPolicy = ownerPolicyService.GetOwnerPolicy(normalizedPath);
        using var handle = pathGuard.OpenExistingManagedObject(
            normalizedPath,
            ProgramDataObjectKind.File,
            ProgramDataManagedObjectAccess.DaclRepair);

        var beforeSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: false);
        var beforeOwner = GetOwnerSid(beforeSecurity);
        bool ownerChanged = !ownerPolicyService.IsAllowedOwner(beforeOwner, ownerPolicy);
        bool removedUntrusted = ownerChanged || securityVerifier.HasUntrustedFileWriteOrOwnerAccess(beforeSecurity, aclProfile);

        ownerRepairService.RepairOwner(handle, normalizedPath, isDirectory: false, ownerPolicy);
        securityApplier.ApplyFileAcl(handle, normalizedPath, aclProfile);
        securityVerifier.VerifyFileSecurity(handle, aclProfile, ownerPolicy);

        var afterSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: false);
        bool daclChanged = ProgramDataAclRuleHelper.GetSecuritySignature(beforeSecurity) !=
                           ProgramDataAclRuleHelper.GetSecuritySignature(afterSecurity);
        return new ProgramDataSecurityRepairResult(ownerChanged, removedUntrusted, daclChanged);
    }

    public bool EnsureManagedFileOwner(string filePath)
        => EnsureManagedFileOwner(filePath, []);

    public bool EnsureManagedDirectoryOwner(string directoryPath)
        => EnsureManagedDirectoryOwner(directoryPath, []);

    public bool EnsureManagedFileOwner(string filePath, IReadOnlyCollection<string> expectedAdditionalOwnerSids)
    {
        var normalizedPath = pathGuard.NormalizeExistingPathUnderRoot(filePath, ProgramDataObjectKind.File);
        using var handle = pathGuard.OpenExistingManagedObject(
            normalizedPath,
            ProgramDataObjectKind.File,
            ProgramDataManagedObjectAccess.OwnerRepair);
        return ownerRepairService.RepairOwner(
            handle,
            normalizedPath,
            isDirectory: false,
            ownerPolicyService.GetOwnerPolicy(normalizedPath),
            NormalizeOwnerSids(expectedAdditionalOwnerSids));
    }

    public bool EnsureManagedDirectoryOwner(string directoryPath, IReadOnlyCollection<string> expectedAdditionalOwnerSids)
    {
        var normalizedPath = pathGuard.NormalizeExistingPathUnderRoot(directoryPath, ProgramDataObjectKind.Directory);
        using var handle = pathGuard.OpenExistingManagedObject(
            normalizedPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.OwnerRepair);
        return ownerRepairService.RepairOwner(
            handle,
            normalizedPath,
            isDirectory: true,
            ownerPolicyService.GetOwnerPolicy(normalizedPath),
            NormalizeOwnerSids(expectedAdditionalOwnerSids));
    }

    private static SecurityIdentifier GetOwnerSid(FileSystemSecurity security)
        => (SecurityIdentifier?)security.GetOwner(typeof(SecurityIdentifier))
           ?? throw new InvalidOperationException("Managed ProgramData object does not have a SID owner.");

    private static IReadOnlyCollection<SecurityIdentifier> NormalizeOwnerSids(IReadOnlyCollection<string> sids)
    {
        var result = new List<SecurityIdentifier>(sids.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sids)
        {
            var identity = new SecurityIdentifier(sid);
            if (seen.Add(identity.Value))
            {
                result.Add(identity);
            }
        }

        return result;
    }
}

using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl;

public class ProgramDataDirectoryProvisioner(
    ILoggingService log,
    ProgramDataPathPolicyCatalog pathPolicyCatalog,
    IProgramDataPathGuard pathGuard,
    ProgramDataDirectoryAclBuilder aclBuilder,
    IHandleSecurityDescriptorAccessor handleSecurityDescriptorAccessor,
    ProgramDataSecurityApplier securityApplier,
    ProgramDataSecurityVerifier securityVerifier,
    ProgramDataOwnerRepairService ownerRepairService,
    IBackupIntentNativeFileSystem nativeFileSystem)
    : IProgramDataDirectoryProvisioningService
{
    public string EnsureRoot()
    {
        var createdRoot = false;
        if (!Directory.Exists(pathPolicyCatalog.RootPath))
        {
            Directory.CreateDirectory(pathPolicyCatalog.RootPath);
            createdRoot = true;
        }

        using var handle = pathGuard.OpenExistingManagedObject(
            pathPolicyCatalog.RootPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.OwnerRepair);
        if (createdRoot)
        {
            var createdSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: true);
            log.Info(
                $"ProgramData security created root directory '{pathPolicyCatalog.RootPath}' with {ProgramDataSecurityChangeFormatter.DescribeSecurityState(createdSecurity)}.");
        }

        ownerRepairService.RepairOwner(
            handle,
            pathPolicyCatalog.RootPath,
            isDirectory: true,
            pathPolicyCatalog.ResolveOwnerPolicy(pathPolicyCatalog.RootPath));
        return pathPolicyCatalog.RootPath;
    }

    public string EnsureKnownDirectory(ProgramDataDirectoryPolicy policy)
        => EnsureSubdirectory(
            Path.GetRelativePath(pathPolicyCatalog.RootPath, pathPolicyCatalog.GetDirectoryPath(policy)),
            policy.Profile);

    public void EnsureKnownDirectoryTreeInheritsFromRoot(ProgramDataDirectoryPolicy policy)
        => EnsureDirectoryTreeInheritsFromRoot(
            pathPolicyCatalog.GetDirectoryPath(policy),
            policy.Profile);

    private string EnsureSubdirectory(string relativePath, ProgramDataDirectoryAclProfile aclProfile)
    {
        var normalizedPath = pathGuard.NormalizeRelativePath(relativePath);
        EnsureRoot();

        var current = pathPolicyCatalog.RootPath;
        foreach (var segment in GetRelativeSegments(normalizedPath))
        {
            current = Path.Combine(current, segment);
            var effectiveProfile = string.Equals(current, normalizedPath, StringComparison.OrdinalIgnoreCase)
                ? pathPolicyCatalog.RegisterDirectoryProfile(current, aclProfile)
                : pathPolicyCatalog.ResolveKnownDirectoryProfile(current) ?? throw new InvalidOperationException(
                    $"Managed ProgramData directory '{current}' did not resolve to a directory profile.");
            EnsureDirectoryUnderRoot(current, effectiveProfile);
        }

        return normalizedPath;
    }

    public void EnsureDirectoryUnderRoot(string directoryPath, ProgramDataDirectoryAclProfile aclProfile)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(directoryPath);
        if (string.Equals(normalizedPath, pathPolicyCatalog.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            EnsureRoot();
            return;
        }

        var createdDirectory = false;
        var parentDirectory = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidOperationException($"Managed ProgramData directory '{normalizedPath}' must have a parent directory.");
        HardenParentChain(parentDirectory);

        if (!Directory.Exists(normalizedPath))
        {
            CreateManagedDirectory(normalizedPath, parentDirectory, aclProfile);
            createdDirectory = true;
        }

        var effectiveProfile = pathPolicyCatalog.RegisterDirectoryProfile(normalizedPath, aclProfile);
        using var handle = pathGuard.OpenExistingManagedObject(
            normalizedPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.DaclRepair);
        if (createdDirectory)
        {
            var createdSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: true);
            log.Info(
                $"ProgramData security created directory '{normalizedPath}' with {ProgramDataSecurityChangeFormatter.DescribeSecurityState(createdSecurity)}.");
        }

        var ownerPolicy = pathPolicyCatalog.ResolveOwnerPolicy(normalizedPath);
        ownerRepairService.RepairOwner(handle, normalizedPath, isDirectory: true, ownerPolicy);
        securityApplier.ApplyDirectoryAcl(handle, normalizedPath, effectiveProfile, existingSecurity: null);
        securityVerifier.VerifyDirectorySecurity(handle, effectiveProfile, ownerPolicy);
    }

    public void EnsureDirectoryTreeInheritsFromRoot(
        string directoryPath,
        ProgramDataDirectoryAclProfile rootAclProfile)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(directoryPath);
        EnsureDirectoryUnderRoot(normalizedPath, rootAclProfile);
        EnsureDirectoryTreeInheritsCore(normalizedPath);
    }

    private void EnsureDirectoryTreeInheritsCore(string normalizedPath)
    {
        var filesChanged = 0;
        var directoriesChanged = 0;
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(normalizedPath);
        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var childFilePath in Directory.EnumerateFiles(currentDirectory))
            {
                if (EnsureChildInheritsAcl(childFilePath, ProgramDataObjectKind.File))
                {
                    filesChanged++;
                }
            }

            foreach (var childDirectoryPath in Directory.EnumerateDirectories(currentDirectory))
            {
                if (EnsureChildInheritsAcl(childDirectoryPath, ProgramDataObjectKind.Directory))
                {
                    directoriesChanged++;
                }

                pendingDirectories.Push(childDirectoryPath);
            }
        }

        if (directoriesChanged > 0 || filesChanged > 0)
        {
            log.Info(
                $"ProgramData security propagated inherited ACLs under '{normalizedPath}': directories {directoriesChanged}, files {filesChanged}.");
        }

        bool EnsureChildInheritsAcl(string path, ProgramDataObjectKind kind)
        {
            var childNormalizedPath = pathGuard.NormalizeExistingPathUnderRoot(path, kind);
            using var childHandle = pathGuard.OpenExistingManagedObject(
                childNormalizedPath,
                kind,
                ProgramDataManagedObjectAccess.DaclRepair);
            ownerRepairService.RepairOwner(
                childHandle,
                childNormalizedPath,
                isDirectory: kind == ProgramDataObjectKind.Directory,
                pathPolicyCatalog.ResolveOwnerPolicy(childNormalizedPath));
            return handleSecurityDescriptorAccessor.ModifyAclWithFallback(
                childHandle,
                isFolder: kind == ProgramDataObjectKind.Directory,
                security =>
                {
                    if (!security.AreAccessRulesProtected &&
                        security.AreAccessRulesCanonical &&
                        !ProgramDataAclRuleHelper.GetExplicitRules(security).Any())
                    {
                        return false;
                    }

                    FileSystemSecurity target = security is DirectorySecurity ? new DirectorySecurity() : new FileSecurity();
                    target.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                    security.SetSecurityDescriptorSddlForm(
                        target.GetSecurityDescriptorSddlForm(AccessControlSections.Access),
                        AccessControlSections.Access);
                    return true;
                });
        }

    }

    public void EnsureTraverseOnlyAccess(string directoryPath, string sid, ProgramDataDirectoryAclProfile aclProfile)
    {
        EnsureDirectoryUnderRoot(directoryPath, aclProfile);
        var normalizedPath = pathGuard.NormalizeExistingPathUnderRoot(directoryPath, ProgramDataObjectKind.Directory);
        if (string.Equals(normalizedPath, pathPolicyCatalog.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var handle = pathGuard.OpenExistingManagedObject(
            normalizedPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.DaclRepair);
        var targetSid = new System.Security.Principal.SecurityIdentifier(sid);
        securityApplier.ApplyTraverseOnlyAccess(handle, normalizedPath, aclProfile, targetSid);

        var appliedSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: true);
        if (!securityVerifier.HasExactTraverseAce(appliedSecurity, targetSid))
        {
            throw new InvalidOperationException(
                $"Managed ProgramData directory '{normalizedPath}' is missing exact traverse-only access for '{sid}'.");
        }
    }

    private void HardenParentChain(string parentDirectory)
    {
        EnsureRoot();
        if (string.Equals(parentDirectory, pathPolicyCatalog.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var current = pathPolicyCatalog.RootPath;
        foreach (var segment in GetRelativeSegments(parentDirectory))
        {
            using var _ = pathGuard.OpenExistingManagedObject(
                current,
                ProgramDataObjectKind.Directory,
                ProgramDataManagedObjectAccess.Validate);
            current = Path.Combine(current, segment);
            var directoryProfile = pathPolicyCatalog.ResolveKnownDirectoryProfile(current);
            if (directoryProfile == null)
            {
                if (!pathPolicyCatalog.IsUnderDynamicDirectoryPolicyRoot(current))
                {
                    throw new InvalidOperationException(
                        $"Managed ProgramData directory '{current}' did not resolve to a directory profile.");
                }

                Directory.CreateDirectory(current);
                continue;
            }

            if (pathPolicyCatalog.IsKnownManagedDirectory(current))
            {
                directoryProfile = pathPolicyCatalog.RegisterDirectoryProfile(current, directoryProfile.Value);
            }

            EnsureDirectoryUnderRoot(current, directoryProfile.Value);
        }
    }

    private void CreateManagedDirectory(
        string normalizedPath,
        string parentDirectory,
        ProgramDataDirectoryAclProfile aclProfile)
    {
        using var parentHandle = pathGuard.OpenExistingManagedObject(
            parentDirectory,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.Validate);
        var createSecurity = aclBuilder.BuildDirectorySecurity(
            pathPolicyCatalog.RegisterDirectoryProfile(normalizedPath, aclProfile),
            existingSecurity: null);
        using var _ = nativeFileSystem.CreateRelativeDirectory(
            parentHandle,
            Path.GetFileName(normalizedPath),
            FileSecurityNative.FILE_LIST_DIRECTORY |
            FileSecurityNative.FILE_READ_ATTRIBUTES |
            FileSecurityNative.READ_CONTROL |
            FileSecurityNative.WRITE_DAC |
            FileSecurityNative.WRITE_OWNER,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            createSecurity.GetSecurityDescriptorBinaryForm());
    }

    private IEnumerable<string> GetRelativeSegments(string normalizedPath)
    {
        var relativePath = Path.GetRelativePath(pathPolicyCatalog.RootPath, normalizedPath);
        if (relativePath is "." or "")
        {
            yield break;
        }

        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            yield return segment;
        }
    }

}

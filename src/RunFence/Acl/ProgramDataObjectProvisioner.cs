using System.Security.AccessControl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl;

public sealed class ProgramDataObjectProvisioner(
    ILoggingService log,
    ProgramDataDirectoryProvisioner directoryProvisioner,
    ProgramDataPathPolicyCatalog pathPolicyCatalog,
    IProgramDataPathGuard pathGuard,
    ProgramDataDirectoryAclBuilder aclBuilder,
    IHandleSecurityDescriptorAccessor handleSecurityDescriptorAccessor,
    ProgramDataOwnerRepairService ownerRepairService,
    ProgramDataExplicitAclApplier explicitAclApplier,
    IBackupIntentNativeFileSystem nativeFileSystem)
    : IProgramDataObjectProvisioner
{
    private const uint DesiredFileAccess =
        FileSecurityNative.FILE_READ_DATA |
        FileSecurityNative.FILE_READ_ATTRIBUTES |
        FileSecurityNative.READ_CONTROL |
        FileSecurityNative.WRITE_DAC |
        FileSecurityNative.WRITE_OWNER |
        FileSecurityNative.GENERIC_WRITE;

    public string EnsureDirectory(ProgramDataDirectoryPolicy policy)
        => directoryProvisioner.EnsureKnownDirectory(policy);

    public FileStream CreateOrReplaceFile(ProgramDataFilePolicy policy, FileShare share)
        => CreateOrReplaceFile(
            new ProgramDataExplicitFileRequest(
                pathPolicyCatalog.GetFilePath(policy),
                policy.Profile,
                [],
                share,
                OverwriteExisting: true));

    public void CreateOrRepairDirectory(ProgramDataExplicitDirectoryRequest request)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(request.Path);
        directoryProvisioner.EnsureDirectoryUnderRoot(normalizedPath, request.Profile);

        using var handle = pathGuard.OpenExistingManagedObject(
            normalizedPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.DaclRepair);
        var ownerPolicy = pathPolicyCatalog.ResolveOwnerPolicy(normalizedPath);
        ownerRepairService.RepairOwner(handle, normalizedPath, isDirectory: true, ownerPolicy);

        var existingSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: true);
        var targetSecurity = aclBuilder.BuildRestrictedDirectorySecurity(
            request.Profile,
            request.ReplaceExistingSecurity ? null : existingSecurity,
            request.AdditionalAccess);
        explicitAclApplier.ApplyAcl(handle, normalizedPath, isDirectory: true, targetSecurity);
        explicitAclApplier.VerifyDacl(handle, isDirectory: true, targetSecurity);
    }

    public FileStream CreateOrReplaceFile(ProgramDataExplicitFileRequest request)
    {
        var target = PrepareFileTarget(request.Path);
        uint attributes = FileSecurityNative.GetFileAttributes(target.NormalizedPath);
        if (attributes != FileSecurityNative.INVALID_FILE_ATTRIBUTES)
        {
            if ((attributes & FileSecurityNative.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                throw new InvalidOperationException(
                    $"Explicit ProgramData file path '{target.NormalizedPath}' must not be a reparse point.");
            }

            if ((attributes & FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                throw new InvalidOperationException($"Explicit ProgramData file path '{target.NormalizedPath}' is a directory.");
            }
        }

        var targetSecurity = aclBuilder.BuildRestrictedFileSecurity(request.Profile, request.AdditionalAccess);
        var overwriteExisting = File.Exists(target.NormalizedPath);
        if (overwriteExisting)
        {
            if (!request.OverwriteExisting)
            {
                throw new IOException($"Explicit ProgramData file '{target.NormalizedPath}' already exists.");
            }

            using var existingHandle = pathGuard.OpenExistingManagedObject(
                target.NormalizedPath,
                ProgramDataObjectKind.File,
                ProgramDataManagedObjectAccess.DaclRepair);
            ownerRepairService.RepairOwner(
                existingHandle,
                target.NormalizedPath,
                isDirectory: false,
                pathPolicyCatalog.ResolveOwnerPolicy(target.NormalizedPath));
            explicitAclApplier.ApplyAcl(existingHandle, target.NormalizedPath, isDirectory: false, targetSecurity);
            explicitAclApplier.VerifyDacl(existingHandle, isDirectory: false, targetSecurity);
        }

        using var parentHandle = pathGuard.OpenExistingManagedObject(
            target.ParentDirectory,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.Validate);
        var stream = CreateRelativeFileStream(
            parentHandle,
            target.NormalizedPath,
            targetSecurity,
            MapShareAccess(request.Share),
            overwriteExisting);
        try
        {
            log.Info(
                overwriteExisting
                    ? $"ProgramData security replaced explicit file '{target.NormalizedPath}' with {ProgramDataSecurityChangeFormatter.DescribeSecurityState(targetSecurity)}."
                    : $"ProgramData security created explicit file '{target.NormalizedPath}' with {ProgramDataSecurityChangeFormatter.DescribeSecurityState(targetSecurity)}.");
            RepairOwner(stream, target.NormalizedPath);
            explicitAclApplier.ApplyAcl(stream.SafeFileHandle, target.NormalizedPath, isDirectory: false, targetSecurity);
            explicitAclApplier.VerifyDacl(stream.SafeFileHandle, isDirectory: false, targetSecurity);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void CreateFile(ProgramDataExplicitFileRequest request, Action<Stream> writeContent)
    {
        using var stream = CreateOrReplaceFile(request with { OverwriteExisting = false });
        writeContent(stream);
        stream.Flush();
    }

    private ProgramDataFileTarget PrepareFileTarget(string filePath)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(filePath);
        var parentDirectory = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidOperationException($"Explicit ProgramData file '{normalizedPath}' must have a parent directory.");
        if (!Directory.Exists(parentDirectory))
        {
            throw new InvalidOperationException(
                $"Explicit ProgramData file parent directory '{parentDirectory}' must already exist.");
        }

        return new ProgramDataFileTarget(normalizedPath, parentDirectory);
    }

    private FileStream CreateRelativeFileStream(
        Microsoft.Win32.SafeHandles.SafeFileHandle parentHandle,
        string normalizedPath,
        FileSystemSecurity security,
        uint shareAccess,
        bool overwriteExisting)
    {
        var fileHandle = nativeFileSystem.CreateRelativeFile(
            parentHandle,
            Path.GetFileName(normalizedPath),
            DesiredFileAccess,
            shareAccess,
            overwriteExisting,
            security.GetSecurityDescriptorBinaryForm());
        return new FileStream(fileHandle, FileAccess.ReadWrite);
    }

    private void RepairOwner(FileStream stream, string normalizedPath)
        => ownerRepairService.RepairOwner(
            stream.SafeFileHandle,
            normalizedPath,
            isDirectory: false,
            pathPolicyCatalog.ResolveOwnerPolicy(normalizedPath));

    private static uint MapShareAccess(FileShare share)
    {
        uint shareAccess = 0;
        if ((share & FileShare.Read) != 0)
        {
            shareAccess |= FileSecurityNative.FILE_SHARE_READ;
        }

        if ((share & FileShare.Write) != 0)
        {
            shareAccess |= FileSecurityNative.FILE_SHARE_WRITE;
        }

        if ((share & FileShare.Delete) != 0)
        {
            shareAccess |= FileSecurityNative.FILE_SHARE_DELETE;
        }

        return shareAccess;
    }

    private readonly record struct ProgramDataFileTarget(string NormalizedPath, string ParentDirectory);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ShortcutDestinationEntryAccessorTests
{
    [Fact]
    public void TryCaptureExistingMetadata_UsesOpenReparsePointFlags()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_MetadataFlags");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "placeholder");
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.Archive));
        var accessor = CreateAccessor(nativeApi);

        accessor.TryCaptureExistingMetadata(shortcutPath);

        var openCall = Assert.Single(nativeApi.OpenCalls);
        Assert.Equal(FileSecurityNative.READ_CONTROL, openCall.DesiredAccess);
        Assert.Equal(FileSecurityNative.OPEN_EXISTING, openCall.CreationDisposition);
        Assert.True((openCall.FlagsAndAttributes & FileSecurityNative.FILE_FLAG_OPEN_REPARSE_POINT) != 0);
        Assert.True((openCall.FlagsAndAttributes & FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS) != 0);
    }

    [Fact]
    public void DeleteExistingDestination_UsesOpenReparsePointFlags()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_DeleteFlags");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "placeholder");
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.Archive));
        var accessor = CreateAccessor(nativeApi);

        accessor.DeleteExistingDestination(shortcutPath);

        var openCall = Assert.Single(nativeApi.OpenCalls);
        Assert.Equal(FileSecurityNative.DELETE, openCall.DesiredAccess);
        Assert.True((openCall.FlagsAndAttributes & FileSecurityNative.FILE_FLAG_OPEN_REPARSE_POINT) != 0);
        Assert.Equal(
            FileSecurityNative.FILE_DISPOSITION_FLAGS.DELETE |
            FileSecurityNative.FILE_DISPOSITION_FLAGS.POSIX_SEMANTICS |
            FileSecurityNative.FILE_DISPOSITION_FLAGS.IGNORE_READONLY_ATTRIBUTE,
            Assert.Single(nativeApi.DeleteFlags));
    }

    [Fact]
    public void TryCaptureExistingMetadata_BrokenFinalReparsePoint_ReturnsNull()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_BrokenReparse");
        var shortcutPath = Path.Combine(tempDir.Path, "broken.lnk");
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.ReparsePoint | FileAttributes.Archive));
        nativeApi.ResolvedOpenException = new Win32Exception(2);
        var accessor = CreateAccessor(nativeApi);

        var metadata = accessor.TryCaptureExistingMetadata(shortcutPath);

        Assert.Null(metadata);
    }

    [Fact]
    public void TryCaptureExistingMetadata_ValidNonSymlinkReparsePointDoesNotTreatResolutionFailureAsBroken()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_ValidReparse");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "placeholder");
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.ReparsePoint | FileAttributes.Archive))
        {
            ResolvedOpenException = new IOException("Resolution is not supported for this reparse tag.")
        };
        var accessor = CreateAccessor(nativeApi);

        var metadata = accessor.TryCaptureExistingMetadata(shortcutPath);

        Assert.NotNull(metadata);
    }

    [Fact]
    public void TryCaptureExistingMetadata_DirectoryEntry_ReturnsNull()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_Directory");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "placeholder");
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.Directory | FileAttributes.ReparsePoint));
        var accessor = CreateAccessor(nativeApi);

        var metadata = accessor.TryCaptureExistingMetadata(shortcutPath);

        Assert.Null(metadata);
    }

    [Fact]
    public void TryCaptureExistingMetadata_NonDirectoryReparsePoint_StripsReparseSpecificAttributes()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_ReparseAttributes");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "placeholder");
        var attributes =
            FileAttributes.ReparsePoint |
            FileAttributes.Archive |
            FileAttributes.Hidden |
            FileAttributes.ReadOnly |
            FileAttributes.Offline |
            FileAttributes.Temporary;
        var nativeApi = new FakeShortcutDestinationNativeApi(CreateHandleInfo(attributes));
        var accessor = CreateAccessor(nativeApi);

        var metadata = accessor.TryCaptureExistingMetadata(shortcutPath);

        Assert.NotNull(metadata);
        Assert.Equal(
            FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly,
            metadata!.Attributes);
    }

    [Fact]
    public void DeleteExistingDestination_DirectoryEntry_ThrowsIOException()
    {
        using var tempDir = new TempDirectory("ShortcutDestinationEntryAccessor_DeleteDirectory");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "placeholder");
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.Directory | FileAttributes.ReparsePoint));
        var accessor = CreateAccessor(nativeApi);

        var ex = Assert.Throws<IOException>(() => accessor.DeleteExistingDestination(shortcutPath));

        Assert.Contains("directory entry", ex.Message);
        Assert.Empty(nativeApi.DeleteFlags);
    }

    [Fact]
    public void TryCaptureExistingMetadata_MissingEntry_ReturnsNull()
    {
        var nativeApi = new FakeShortcutDestinationNativeApi(
            CreateHandleInfo(FileAttributes.Archive))
        {
            OpenException = new Win32Exception(2)
        };
        var accessor = CreateAccessor(nativeApi);

        var metadata = accessor.TryCaptureExistingMetadata(@"C:\missing\managed.lnk");

        Assert.Null(metadata);
    }

    private static ShortcutDestinationEntryAccessor CreateAccessor(FakeShortcutDestinationNativeApi nativeApi)
        => new(
            nativeApi,
            new BackupPrivilegeSecurityDescriptorAccessor(new FakeBackupPrivilegeSecurityNative(
                CreateDescriptorBytes())));

    private static FileSecurityNative.BY_HANDLE_FILE_INFORMATION CreateHandleInfo(FileAttributes attributes)
    {
        var creation = new DateTime(2022, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToFileTimeUtc();
        var access = new DateTime(2022, 2, 3, 4, 5, 6, DateTimeKind.Utc).ToFileTimeUtc();
        var write = new DateTime(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc).ToFileTimeUtc();
        return new FileSecurityNative.BY_HANDLE_FILE_INFORMATION
        {
            dwFileAttributes = (uint)attributes,
            ftCreationTime = ToFileTime(creation),
            ftLastAccessTime = ToFileTime(access),
            ftLastWriteTime = ToFileTime(write)
        };
    }

    private static System.Runtime.InteropServices.ComTypes.FILETIME ToFileTime(long value)
        => new()
        {
            dwLowDateTime = (int)(value & 0xFFFFFFFF),
            dwHighDateTime = (int)(value >> 32)
        };

    private static byte[] CreateDescriptorBytes()
    {
        var security = new FileSecurity();
        return security.GetSecurityDescriptorBinaryForm();
    }

    private sealed class FakeShortcutDestinationNativeApi(FileSecurityNative.BY_HANDLE_FILE_INFORMATION fileInformation)
        : IShortcutDestinationNativeApi
    {
        public Exception? OpenException { get; set; }
        public Exception? ResolvedOpenException { get; set; }
        public List<OpenCall> OpenCalls { get; } = [];
        public List<FileSecurityNative.FILE_DISPOSITION_FLAGS> DeleteFlags { get; } = [];

        public SafeFileHandle Open(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
        {
            OpenCalls.Add(new OpenCall(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes));
            if ((flagsAndAttributes & FileSecurityNative.FILE_FLAG_OPEN_REPARSE_POINT) == 0 && ResolvedOpenException != null)
                throw ResolvedOpenException;
            if (OpenException != null)
                throw OpenException;

            return new SafeFileHandle(new IntPtr(1234), ownsHandle: false);
        }

        public FileSecurityNative.BY_HANDLE_FILE_INFORMATION GetFileInformation(SafeFileHandle handle)
            => fileInformation;

        public void SetDeleteDisposition(SafeFileHandle handle, FileSecurityNative.FILE_DISPOSITION_FLAGS flags)
            => DeleteFlags.Add(flags);

        public void SetBasicInfo(SafeFileHandle handle, FileSecurityNative.FILE_BASIC_INFO basicInfo)
            => throw new NotSupportedException();
    }

    private sealed class FakeBackupPrivilegeSecurityNative(byte[] descriptorBytes) : IBackupPrivilegeSecurityNative
    {
        public IntPtr CreateFile(
            string path,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile) => throw new NotSupportedException();

        public void CloseHandle(IntPtr handle)
        {
        }

        public int GetSecurityInfo(
            IntPtr handle,
            FileSecurityNative.SE_OBJECT_TYPE objectType,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            out IntPtr owner,
            out IntPtr group,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr securityDescriptor)
        {
            owner = IntPtr.Zero;
            group = IntPtr.Zero;
            dacl = IntPtr.Zero;
            sacl = IntPtr.Zero;
            securityDescriptor = Marshal.AllocHGlobal(descriptorBytes.Length);
            Marshal.Copy(descriptorBytes, 0, securityDescriptor, descriptorBytes.Length);
            return 0;
        }

        public int SetSecurityInfo(
            IntPtr handle,
            FileSecurityNative.SE_OBJECT_TYPE objectType,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            IntPtr owner,
            IntPtr group,
            IntPtr dacl,
            IntPtr sacl) => throw new NotSupportedException();

        public uint GetSecurityDescriptorLength(IntPtr securityDescriptor)
            => FileSecurityNative.GetSecurityDescriptorLength(securityDescriptor);

        public bool GetSecurityDescriptorDacl(
            IntPtr securityDescriptor,
            out bool daclPresent,
            out IntPtr dacl,
            out bool daclDefaulted)
            => FileSecurityNative.GetSecurityDescriptorDacl(
                securityDescriptor,
                out daclPresent,
                out dacl,
                out daclDefaulted);

        public bool GetSecurityDescriptorOwner(
            IntPtr securityDescriptor,
            out IntPtr owner,
            out bool ownerDefaulted)
            => FileSecurityNative.GetSecurityDescriptorOwner(
                securityDescriptor,
                out owner,
                out ownerDefaulted);

        public void LocalFree(IntPtr securityDescriptor)
            => Marshal.FreeHGlobal(securityDescriptor);
    }

    private sealed record OpenCall(
        string Path,
        uint DesiredAccess,
        uint ShareMode,
        uint CreationDisposition,
        uint FlagsAndAttributes);
}

using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class BackupIntentFileSystemTests
{
    [Fact]
    public void GetPathState_InvalidRootedPathSyntax_ReturnsUnknownForFileAndDirectory()
    {
        var fileSystem = new BackupIntentFileSystem(new BackupIntentNativeFileSystem(), new BackupIntentManagedFileSystemProbe());
        var path = "C:\\bad\0tool.exe";

        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetFileState(path));
        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetDirectoryState(path));
    }

    [Fact]
    public void GetPathState_OverlongRootedPath_ReturnsUnknownForFileAndDirectory()
    {
        var fileSystem = new BackupIntentFileSystem(new BackupIntentNativeFileSystem(), new BackupIntentManagedFileSystemProbe());
        var path = @"C:\" + new string('a', 40000);

        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetFileState(path));
        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetDirectoryState(path));
    }

    [Theory]
    [InlineData(BackupIntentNativeStatus.StatusAccessDenied)]
    [InlineData(BackupIntentNativeStatus.StatusSharingViolation)]
    [InlineData(BackupIntentNativeStatus.StatusDeletePending)]
    [InlineData(BackupIntentNativeStatus.StatusCannotDelete)]
    [InlineData(BackupIntentNativeStatus.StatusPrivilegeNotHeld)]
    [InlineData(BackupIntentNativeStatus.StatusObjectPathInvalid)]
    public void GetPathState_UnknownNativeStatuses_ReturnUnknown(int status)
    {
        var fileSystem = CreateFileSystem(nativeStatus: status);

        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetFileState(@"C:\Apps\tool.exe"));
        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetDirectoryState(@"C:\Apps"));
    }

    [Theory]
    [InlineData(BackupIntentNativeStatus.StatusObjectNameNotFound, false)]
    [InlineData(BackupIntentNativeStatus.StatusObjectPathNotFound, false)]
    [InlineData(BackupIntentNativeStatus.StatusNoSuchFile, false)]
    [InlineData(BackupIntentNativeStatus.StatusNoSuchDevice, false)]
    [InlineData(BackupIntentNativeStatus.StatusFileIsADirectory, false)]
    [InlineData(BackupIntentNativeStatus.StatusObjectNameNotFound, true)]
    [InlineData(BackupIntentNativeStatus.StatusObjectPathNotFound, true)]
    [InlineData(BackupIntentNativeStatus.StatusNoSuchFile, true)]
    [InlineData(BackupIntentNativeStatus.StatusNoSuchDevice, true)]
    [InlineData(BackupIntentNativeStatus.StatusNotADirectory, true)]
    public void GetPathState_MissingNativeStatuses_ReturnMissing(int status, bool directory)
    {
        var fileSystem = CreateFileSystem(nativeStatus: status);

        var result = directory
            ? fileSystem.GetDirectoryState(@"C:\Apps")
            : fileSystem.GetFileState(@"C:\Apps\tool.exe");

        Assert.Equal(BackupIntentPathState.Missing, result);
    }

    public static TheoryData<Func<Exception>> UnknownFallbackExceptions =>
    [
        () => new UnauthorizedAccessException(),
        () => new IOException(),
        () => new NotSupportedException(),
        () => new SecurityException(),
        () => new ArgumentException(),
        () => new PathTooLongException()
    ];

    [Theory]
    [MemberData(nameof(UnknownFallbackExceptions))]
    public void GetPathState_UnknownManagedFallbackExceptions_ReturnUnknown(Func<Exception> exceptionFactory)
    {
        var fileSystem = CreateFileSystem(
            nativeStatus: unchecked((int)0xC0000001),
            attributesException: exceptionFactory());

        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetFileState(@"C:\Apps\tool.exe"));
        Assert.Equal(BackupIntentPathState.Unknown, fileSystem.GetDirectoryState(@"C:\Apps"));
    }

    public static TheoryData<Func<Exception>> MissingFallbackExceptions =>
    [
        () => new FileNotFoundException(),
        () => new DirectoryNotFoundException()
    ];

    [Theory]
    [MemberData(nameof(MissingFallbackExceptions))]
    public void GetPathState_MissingManagedFallbackExceptions_ReturnMissing(Func<Exception> exceptionFactory)
    {
        var fileSystem = CreateFileSystem(
            nativeStatus: unchecked((int)0xC0000001),
            attributesException: exceptionFactory());

        Assert.Equal(BackupIntentPathState.Missing, fileSystem.GetFileState(@"C:\Apps\tool.exe"));
        Assert.Equal(BackupIntentPathState.Missing, fileSystem.GetDirectoryState(@"C:\Apps"));
    }

    [Fact]
    public void TryEnumerateDirectories_ManagedFallbackFailure_ReturnsFalse()
    {
        var native = new FakeNativeFileSystem
        {
            DirectoryOpenResultFactory = _ => CreateSuccessOpenResult(),
            EnumerateDirectoriesResult = false
        };
        var managed = new FakeManagedFileSystemProbe
        {
            EnumerateDirectoriesException = new IOException()
        };
        var fileSystem = new BackupIntentFileSystem(native, managed);

        var success = fileSystem.TryEnumerateDirectories(@"C:\Apps", out var directories);

        Assert.False(success);
        Assert.Empty(directories);
    }

    [Fact]
    public void UsesNativeOpenForFilesAndDirectoryEnumeration()
    {
        var root = Path.Combine(Path.GetTempPath(), "RunFencePackageFsTests", Guid.NewGuid().ToString("N"));
        var childDirectory = Path.Combine(root, "ChildPackage_1.0.0.0_x64__publisher");
        var filePath = Path.Combine(root, "tool.exe");
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(filePath, string.Empty);
        try
        {
            var fileSystem = new BackupIntentFileSystem(new BackupIntentNativeFileSystem(), new BackupIntentManagedFileSystemProbe());

            Assert.Equal(BackupIntentPathState.Exists, fileSystem.GetFileState(filePath));
            Assert.Equal(BackupIntentPathState.Missing, fileSystem.GetFileState(Path.Combine(root, "missing.exe")));
            Assert.True(fileSystem.TryEnumerateDirectories(root, out var directories));
            Assert.Contains(childDirectory, directories, StringComparer.OrdinalIgnoreCase);
            Assert.True(fileSystem.TryGetDirectoryLastWriteTimeUtc(childDirectory, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateRelativeDirectory_And_CreateRelativeFile_CreatesDirectoryAndOverwritesFileContent()
    {
        using var tempDirectory = new TempDirectory("RunFence_BackupIntentNativeFileSystem");
        var parentPath = Path.Combine(tempDirectory.Path, "apps");
        Directory.CreateDirectory(parentPath);
        var fileSystem = new BackupIntentNativeFileSystem();
        using var parentHandleResult = fileSystem.TryOpen(parentPath, directory: true);
        Assert.True(parentHandleResult.IsSuccess);
        var parentHandle = parentHandleResult.Handle!;

        using var childDirectoryHandle = fileSystem.CreateRelativeDirectory(
            parentHandle,
            "Versioned",
            FileSecurityNative.FILE_LIST_DIRECTORY | FileSecurityNative.FILE_READ_ATTRIBUTES,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            null);

        var childDirectoryPath = Path.Combine(parentPath, "Versioned");
        Assert.True(Directory.Exists(childDirectoryPath));

        using var newFileHandle = fileSystem.CreateRelativeFile(
            childDirectoryHandle,
            "tool.txt",
            FileSecurityNative.FILE_READ_DATA | FileSecurityNative.GENERIC_WRITE,
            FileSecurityNative.FILE_SHARE_READ,
            overwrite: false,
            null);
        using (var stream = new FileStream(newFileHandle, FileAccess.ReadWrite))
        {
            stream.WriteByte(1);
        }

        var childFilePath = Path.Combine(childDirectoryPath, "tool.txt");
        Assert.True(File.Exists(childFilePath));

        using var overwrittenFileHandle = fileSystem.CreateRelativeFile(
            childDirectoryHandle,
            "tool.txt",
            FileSecurityNative.FILE_READ_DATA | FileSecurityNative.GENERIC_WRITE,
            FileSecurityNative.FILE_SHARE_READ,
            overwrite: true,
            null);
        using (var stream = new FileStream(overwrittenFileHandle, FileAccess.ReadWrite))
        {
            stream.WriteByte(2);
        }

        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(childFilePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    public void CreateRelativeDirectory_InvalidChildName_ThrowsArgumentException(string childName)
    {
        using var tempDirectory = new TempDirectory("RunFence_BackupIntentNativeFileSystem");
        var parentPath = Path.Combine(tempDirectory.Path, "apps");
        Directory.CreateDirectory(parentPath);
        var fileSystem = new BackupIntentNativeFileSystem();
        using var parentHandleResult = fileSystem.TryOpen(parentPath, directory: true);
        var parentHandle = parentHandleResult.Handle!;

        Assert.Throws<ArgumentException>(() => fileSystem.CreateRelativeDirectory(
            parentHandle,
            childName,
            FileSecurityNative.FILE_LIST_DIRECTORY | FileSecurityNative.FILE_READ_ATTRIBUTES,
            FileSecurityNative.FILE_SHARE_READ,
            null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    public void CreateRelativeFile_InvalidChildName_ThrowsArgumentException(string childName)
    {
        using var tempDirectory = new TempDirectory("RunFence_BackupIntentNativeFileSystem");
        var parentPath = Path.Combine(tempDirectory.Path, "apps");
        Directory.CreateDirectory(parentPath);
        var fileSystem = new BackupIntentNativeFileSystem();
        using var parentHandleResult = fileSystem.TryOpen(parentPath, directory: true);
        var parentHandle = parentHandleResult.Handle!;

        Assert.Throws<ArgumentException>(() => fileSystem.CreateRelativeFile(
            parentHandle,
            childName,
            FileSecurityNative.FILE_READ_DATA,
            FileSecurityNative.FILE_SHARE_READ,
            overwrite: false,
            null));
    }

    [Fact]
    public void CreateRelativeFile_AppliesProvidedSecurityDescriptor_WhenCreated()
    {
        using var tempDirectory = new TempDirectory("RunFence_BackupIntentNativeFileSystem");
        var parentPath = Path.Combine(tempDirectory.Path, "apps");
        Directory.CreateDirectory(parentPath);
        var fileSystem = new BackupIntentNativeFileSystem();
        var currentSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current test user SID was not available.");

        var fileSecurity = new FileSecurity();
        fileSecurity.SetOwner(currentSid);
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            currentSid,
            FileSystemRights.WriteData | FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        var descriptor = fileSecurity.GetSecurityDescriptorBinaryForm();

        using var parentHandleResult = fileSystem.TryOpen(parentPath, directory: true);
        var parentHandle = parentHandleResult.Handle!;
        using var fileHandle = fileSystem.CreateRelativeFile(
            parentHandle,
            "state.dat",
            FileSecurityNative.FILE_READ_DATA | FileSecurityNative.READ_CONTROL,
            FileSecurityNative.FILE_SHARE_READ,
            overwrite: false,
            securityDescriptor: descriptor);

        var createdPath = Path.Combine(parentPath, "state.dat");
        var accessControl = new FileInfo(createdPath).GetAccessControl(AccessControlSections.Access);
        var explicitRules = accessControl
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

        Assert.True(accessControl.AreAccessRulesProtected);
        var matchingRule = Assert.Single(explicitRules, rule =>
            ((SecurityIdentifier)rule.IdentityReference).Value == currentSid.Value);
        Assert.Equal(AccessControlType.Allow, matchingRule.AccessControlType);
        Assert.Equal(
            FileSystemRights.WriteData | FileSystemRights.ReadData,
            matchingRule.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.ReadData));
        Assert.Equal(InheritanceFlags.None, matchingRule.InheritanceFlags);
        Assert.Equal(PropagationFlags.None, matchingRule.PropagationFlags);
    }

    private static BackupIntentFileSystem CreateFileSystem(int nativeStatus, Exception? attributesException = null)
    {
        var native = new FakeNativeFileSystem
        {
            FileOpenResultFactory = _ => new BackupIntentNativeOpenResult(null, nativeStatus),
            DirectoryOpenResultFactory = _ => new BackupIntentNativeOpenResult(null, nativeStatus)
        };
        var managed = new FakeManagedFileSystemProbe
        {
            AttributesException = attributesException
        };
        return new BackupIntentFileSystem(native, managed);
    }

    private static BackupIntentNativeOpenResult CreateSuccessOpenResult()
        => new(new SafeFileHandle(new IntPtr(1), ownsHandle: false), 0);

    private sealed class FakeNativeFileSystem : IBackupIntentNativeFileSystem
    {
        public Func<string, BackupIntentNativeOpenResult>? FileOpenResultFactory { get; init; }

        public Func<string, BackupIntentNativeOpenResult>? DirectoryOpenResultFactory { get; init; }

        public bool EnumerateDirectoriesResult { get; init; }

        public IReadOnlyList<string> EnumeratedDirectories { get; init; } = [];

        public bool TryGetLastWriteTimeResult { get; init; }

        public DateTime LastWriteTimeUtc { get; init; }

        public BackupIntentNativeOpenResult TryOpen(string path, bool directory)
            => directory
                ? DirectoryOpenResultFactory?.Invoke(path) ?? new BackupIntentNativeOpenResult(null, BackupIntentNativeStatus.StatusObjectNameNotFound)
                : FileOpenResultFactory?.Invoke(path) ?? new BackupIntentNativeOpenResult(null, BackupIntentNativeStatus.StatusObjectNameNotFound);

        public SafeFileHandle CreateRelativeDirectory(
            SafeFileHandle parentHandle,
            string childName,
            uint desiredAccess,
            uint shareAccess,
            byte[]? securityDescriptor = null) => throw new NotSupportedException();

        public SafeFileHandle CreateRelativeFile(
            SafeFileHandle parentHandle,
            string childName,
            uint desiredAccess,
            uint shareAccess,
            bool overwrite,
            byte[]? securityDescriptor = null) => throw new NotSupportedException();

        public bool TryEnumerateDirectories(SafeFileHandle handle, string rootPath, out IReadOnlyList<string> directories)
        {
            directories = EnumeratedDirectories;
            return EnumerateDirectoriesResult;
        }

        public bool TryGetLastWriteTimeUtc(SafeFileHandle handle, out DateTime lastWriteTimeUtc)
        {
            lastWriteTimeUtc = LastWriteTimeUtc;
            return TryGetLastWriteTimeResult;
        }
    }

    private sealed class FakeManagedFileSystemProbe : IBackupIntentManagedFileSystemProbe
    {
        public Exception? AttributesException { get; init; }

        public FileAttributes Attributes { get; init; }

        public Exception? EnumerateDirectoriesException { get; init; }

        public IReadOnlyList<string> EnumerateDirectoriesResult { get; init; } = [];

        public Exception? LastWriteTimeException { get; init; }

        public DateTime LastWriteTimeUtc { get; init; }

        public FileAttributes GetAttributes(string path)
        {
            if (AttributesException != null)
                throw AttributesException;

            return Attributes;
        }

        public IReadOnlyList<string> EnumerateDirectories(string path)
        {
            if (EnumerateDirectoriesException != null)
                throw EnumerateDirectoriesException;

            return EnumerateDirectoriesResult;
        }

        public DateTime GetDirectoryLastWriteTimeUtc(string path)
        {
            if (LastWriteTimeException != null)
                throw LastWriteTimeException;

            return LastWriteTimeUtc;
        }
    }
}

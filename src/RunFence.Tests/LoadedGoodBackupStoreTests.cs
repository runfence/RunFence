using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class LoadedGoodBackupStoreTests : IDisposable
{
    private readonly TempDirectory _tempDir = new("RunFence_LoadedGoodBackupStoreTests");
    private readonly PersistenceFileSecurityMirror _securityMirror = new();
    private readonly LoadedGoodBackupStore _store;

    public LoadedGoodBackupStoreTests()
    {
        _store = new LoadedGoodBackupStore(
            new PersistenceAtomicFileWriter(_securityMirror),
            _securityMirror);
    }

    public void Dispose() => _tempDir.Dispose();

    [Fact]
    public void TryPreserveCurrentFile_WritesBackupFile()
    {
        var targetPath = Path.Combine(_tempDir.Path, "config.dat");
        var bytes = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(targetPath, bytes);

        var preserved = _store.TryPreserveCurrentFile(targetPath, out var warning);

        Assert.True(preserved);
        Assert.Null(warning);
        Assert.Equal(bytes, File.ReadAllBytes(targetPath + ".lastgood"));
    }

    [Fact]
    public void Restore_ReplacesTargetWithBackupContents()
    {
        var targetPath = Path.Combine(_tempDir.Path, "config.dat");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        _store.TryPreserveCurrentFile(targetPath, out _);
        File.WriteAllBytes(targetPath, [9, 9, 9]);

        _store.Restore(targetPath);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void TryPreserveCurrentFile_WritesBackupFileWithSourceSecurityMetadata()
    {
        var targetPath = Path.Combine(_tempDir.Path, "secure.dat");
        File.WriteAllBytes(targetPath, [9, 8, 7]);
        var currentUserSid = WindowsIdentity.GetCurrent().User!;
        var sourceSecurity = new FileSecurity();
        sourceSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sourceSecurity.SetOwner(currentUserSid);
        sourceSecurity.AddAccessRule(new FileSystemAccessRule(
            currentUserSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(targetPath).SetAccessControl(sourceSecurity);

        var preserved = _store.TryPreserveCurrentFile(targetPath, out var warning);

        Assert.True(preserved);
        Assert.Null(warning);
        var backupSecurity = new FileInfo(targetPath + ".lastgood")
            .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        Assert.Equal(sourceSecurity.AreAccessRulesProtected, backupSecurity.AreAccessRulesProtected);
        Assert.Equal(
            sourceSecurity.GetOwner(typeof(SecurityIdentifier)),
            backupSecurity.GetOwner(typeof(SecurityIdentifier)));
        var sourceRule = sourceSecurity.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Single(rule =>
                string.Equals(rule.IdentityReference.Value, currentUserSid.Value, StringComparison.OrdinalIgnoreCase) &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.WriteData)) ==
                (FileSystemRights.ReadData | FileSystemRights.WriteData));
        var backupRule = backupSecurity.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Single(rule =>
                string.Equals(rule.IdentityReference.Value, currentUserSid.Value, StringComparison.OrdinalIgnoreCase) &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.WriteData)) ==
                (FileSystemRights.ReadData | FileSystemRights.WriteData));
        Assert.Equal(sourceRule.IdentityReference.Value, backupRule.IdentityReference.Value);
        Assert.Equal(
            sourceRule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.WriteData),
            backupRule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.WriteData));
        Assert.Equal(sourceRule.AccessControlType, backupRule.AccessControlType);
    }

    [Fact]
    public void TryPreserveCurrentFile_CopiesCurrentBytesWithSourceSecurityMetadata()
    {
        var targetPath = Path.Combine(_tempDir.Path, "current-secure.dat");
        var expectedSecurity = new FileSecurity();
        var writer = new RecordingAtomicFileWriter();
        var mirror = new RecordingPersistenceFileSecurityMirror(expectedSecurity);
        var store = new LoadedGoodBackupStore(writer, mirror);

        var preserved = store.TryPreserveCurrentFile(targetPath, out var warning);

        Assert.True(preserved);
        Assert.Null(warning);
        Assert.Equal(targetPath, writer.SourcePath);
        Assert.Equal(targetPath + ".lastgood", writer.TargetPath);
        Assert.Same(expectedSecurity, writer.FinalSecurity);
    }

    [Fact]
    public void TryPreserveCurrentFile_WhenApplyFileSecurityFails_ReturnsFalseAndPreservesPreviousBackup()
    {
        var targetPath = Path.Combine(_tempDir.Path, "config.dat");
        var backupPath = targetPath + ".lastgood";
        File.WriteAllBytes(targetPath, [4, 5, 6]);
        File.WriteAllBytes(backupPath, [9, 9, 9]);
        var mirror = new ThrowingPersistenceFileSecurityMirror
        {
            CapturedSecurity = new FileSecurity()
        };
        var store = new LoadedGoodBackupStore(new PersistenceAtomicFileWriter(mirror), mirror);

        var preserved = store.TryPreserveCurrentFile(targetPath, out var warning);

        Assert.False(preserved);
        Assert.NotNull(warning);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(backupPath));
    }

    [Fact]
    public void TryPreserveCurrentFile_WhenCurrentTargetCannotBeCopied_ReturnsWarning()
    {
        var targetPath = Path.Combine(_tempDir.Path, "missing.dat");

        var preserved = _store.TryPreserveCurrentFile(targetPath, out var warning);

        Assert.False(preserved);
        Assert.NotNull(warning);
        Assert.Contains("Could not preserve loaded-good backup", warning, StringComparison.Ordinal);
        Assert.False(File.Exists(targetPath + ".lastgood"));
    }

    private sealed class ThrowingPersistenceFileSecurityMirror : IPersistenceFileSecurityMirror
    {
        public FileSecurity CapturedSecurity { get; set; } = new();

        public FileSecurity CaptureFileSecurity(string sourcePath) => CapturedSecurity;

        public void ApplyFileSecurity(string destinationPath, FileSecurity security)
            => throw new UnauthorizedAccessException("apply failed");
    }

    private sealed class RecordingPersistenceFileSecurityMirror(FileSecurity capturedSecurity) : IPersistenceFileSecurityMirror
    {
        public FileSecurity CaptureFileSecurity(string sourcePath) => capturedSecurity;

        public void ApplyFileSecurity(string destinationPath, FileSecurity security)
            => throw new NotSupportedException();
    }

    private sealed class RecordingAtomicFileWriter : IPersistenceAtomicFileWriter
    {
        public string? SourcePath { get; private set; }
        public string? TargetPath { get; private set; }
        public FileSecurity? FinalSecurity { get; private set; }

        public void AtomicWrite(string targetPath, byte[] data)
            => throw new NotSupportedException();

        public void AtomicWrite(string targetPath, byte[] data, FileSecurity? finalSecurity)
            => throw new NotSupportedException();

        public void AtomicCopy(string sourcePath, string targetPath, FileSecurity? finalSecurity)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
            FinalSecurity = finalSecurity;
        }

        public void AtomicWriteBatch(IReadOnlyList<(string path, byte[] data)> files)
            => throw new NotSupportedException();
    }
}

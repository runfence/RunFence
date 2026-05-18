using System.Security.AccessControl;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class PersistenceAtomicFileWriterTests : IDisposable
{
    private readonly TempDirectory _tempDir = new("RunFence_PersistenceAtomicFileWriterTests");
    private readonly RecordingPersistenceFileSecurityMirror _securityMirror = new();
    private readonly PersistenceAtomicFileWriter _writer;

    public PersistenceAtomicFileWriterTests()
    {
        _writer = new PersistenceAtomicFileWriter(_securityMirror);
    }

    public void Dispose() => _tempDir.Dispose();

    [Fact]
    public void AtomicWriteBatch_WhenLaterWriteFails_RollsBackPreExistingFiles()
    {
        var credentialsPath = Path.Combine(_tempDir.Path, "credentials.dat");
        var configPath = Path.Combine(_tempDir.Path, "config.dat");
        var extra1Path = Path.Combine(_tempDir.Path, "extra1.dat");

        File.WriteAllBytes(credentialsPath, [0x01, 0x02, 0x03]);
        File.WriteAllBytes(configPath, [0x11, 0x12, 0x13]);
        File.WriteAllBytes(extra1Path, [0x21, 0x22, 0x23]);

        var credentialsBefore = File.ReadAllBytes(credentialsPath);
        var configBefore = File.ReadAllBytes(configPath);
        var extra1Before = File.ReadAllBytes(extra1Path);

        var failingExtraPath = Path.Combine(_tempDir.Path, "extra2.dat");
        Directory.CreateDirectory(failingExtraPath);

        var files = new List<(string path, byte[] data)>
        {
            (credentialsPath, [0xA1, 0xA2]),
            (configPath, [0xB1, 0xB2]),
            (extra1Path, [0xC1, 0xC2]),
            (failingExtraPath, [0xD1, 0xD2])
        };

        Assert.ThrowsAny<Exception>(() => _writer.AtomicWriteBatch(files));

        Assert.Equal(credentialsBefore, File.ReadAllBytes(credentialsPath));
        Assert.Equal(configBefore, File.ReadAllBytes(configPath));
        Assert.Equal(extra1Before, File.ReadAllBytes(extra1Path));
    }

    [Fact]
    public void AtomicWriteBatch_WhenConfigWriteFails_RollsBackCredentials()
    {
        var credentialsPath = Path.Combine(_tempDir.Path, "credentials.dat");
        File.WriteAllBytes(credentialsPath, [0xAA, 0xBB, 0xCC]);
        var credentialsBefore = File.ReadAllBytes(credentialsPath);

        var configPath = Path.Combine(_tempDir.Path, "config.dat");
        Directory.CreateDirectory(configPath);

        var files = new List<(string path, byte[] data)>
        {
            (credentialsPath, [0x11, 0x22, 0x33]),
            (configPath, [0x44, 0x55, 0x66])
        };

        Assert.ThrowsAny<Exception>(() => _writer.AtomicWriteBatch(files));

        Assert.Equal(credentialsBefore, File.ReadAllBytes(credentialsPath));
    }

    [Fact]
    public void AtomicWrite_WithFinalSecurity_AppliesSecurityAfterWrite()
    {
        var targetPath = Path.Combine(_tempDir.Path, "secure.dat");
        var security = new FileSecurity();

        _writer.AtomicWrite(targetPath, [1, 2, 3], security);

        Assert.Single(_securityMirror.ApplyCalls);
        Assert.Equal(targetPath, _securityMirror.ApplyCalls[0].DestinationPath);
        Assert.Same(security, _securityMirror.ApplyCalls[0].Security);
        Assert.Equal(new byte[] { 1, 2, 3 }, _securityMirror.ApplyCalls[0].ObservedBytes);
    }

    [Fact]
    public void AtomicWrite_WithFinalSecurityApplyFailureAfterReplace_RestoresPreviousTargetAndRethrows()
    {
        var targetPath = Path.Combine(_tempDir.Path, "replace.dat");
        File.WriteAllBytes(targetPath, [9, 9, 9]);
        _securityMirror.ApplyException = new UnauthorizedAccessException("apply failed");

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            _writer.AtomicWrite(targetPath, [1, 2, 3], new FileSecurity()));

        Assert.Equal("apply failed", ex.Message);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void AtomicWrite_WithFinalSecurityApplyFailureAfterCreate_DeletesNewTargetAndRethrows()
    {
        var targetPath = Path.Combine(_tempDir.Path, "create.dat");
        _securityMirror.ApplyException = new UnauthorizedAccessException("apply failed");

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            _writer.AtomicWrite(targetPath, [1, 2, 3], new FileSecurity()));

        Assert.Equal("apply failed", ex.Message);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public void AtomicCopy_WhenTargetExists_ReplacesExistingTarget()
    {
        var sourcePath = Path.Combine(_tempDir.Path, "source.dat");
        var targetPath = Path.Combine(_tempDir.Path, "target.dat");
        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.WriteAllBytes(targetPath, [9, 9, 9]);

        _writer.AtomicCopy(sourcePath, targetPath, finalSecurity: null);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void AtomicCopy_WhenTargetDoesNotExist_CreatesNewTarget()
    {
        var sourcePath = Path.Combine(_tempDir.Path, "copy-source.dat");
        var targetPath = Path.Combine(_tempDir.Path, "copy-target.dat");
        File.WriteAllBytes(sourcePath, [4, 5, 6]);

        _writer.AtomicCopy(sourcePath, targetPath, finalSecurity: null);

        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void AtomicCopy_WithFinalSecurity_AppliesSecurityAfterCopy()
    {
        var sourcePath = Path.Combine(_tempDir.Path, "secure-source.dat");
        var targetPath = Path.Combine(_tempDir.Path, "secure-target.dat");
        var security = new FileSecurity();
        File.WriteAllBytes(sourcePath, [7, 8, 9]);

        _writer.AtomicCopy(sourcePath, targetPath, security);

        Assert.Single(_securityMirror.ApplyCalls);
        Assert.Equal(targetPath, _securityMirror.ApplyCalls[0].DestinationPath);
        Assert.Same(security, _securityMirror.ApplyCalls[0].Security);
        Assert.Equal(new byte[] { 7, 8, 9 }, _securityMirror.ApplyCalls[0].ObservedBytes);
    }

    [Fact]
    public void AtomicCopy_WithFinalSecurityApplyFailure_RestoresPreviousTargetAndRethrows()
    {
        var sourcePath = Path.Combine(_tempDir.Path, "failing-copy-source.dat");
        var targetPath = Path.Combine(_tempDir.Path, "failing-copy-target.dat");
        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.WriteAllBytes(targetPath, [9, 9, 9]);
        _securityMirror.ApplyException = new UnauthorizedAccessException("apply failed");

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            _writer.AtomicCopy(sourcePath, targetPath, new FileSecurity()));

        Assert.Equal("apply failed", ex.Message);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(targetPath));
    }

    private sealed class RecordingPersistenceFileSecurityMirror : IPersistenceFileSecurityMirror
    {
        public Exception? ApplyException { get; set; }
        public List<ApplyCall> ApplyCalls { get; } = [];

        public FileSecurity CaptureFileSecurity(string sourcePath) => new();

        public void ApplyFileSecurity(string destinationPath, FileSecurity security)
        {
            ApplyCalls.Add(new ApplyCall(destinationPath, security, File.ReadAllBytes(destinationPath)));
            if (ApplyException != null)
                throw ApplyException;
        }
    }

    private sealed record ApplyCall(string DestinationPath, FileSecurity Security, byte[] ObservedBytes);
}

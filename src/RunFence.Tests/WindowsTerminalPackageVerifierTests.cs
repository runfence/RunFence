using System.IO.Compression;
using RunFence.Account.UI;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalPackageVerifierTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalPackageVerifier");

    [Fact]
    public void VerifyPackage_WhenZipContainsWindowsTerminalExecutable_VerifiesExtractedExecutable()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(
            zipPath,
            PeEntry("terminal-1.0/WindowsTerminal.exe"),
            PeEntry("terminal-1.0/Microsoft.Terminal.Control.dll"),
            PeEntry("terminal-1.0/native-code.bin"),
            TextEntry("terminal-1.0/resources.pri", "resources"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var verifier = CreateVerifier(signatureVerifier);

        verifier.VerifyPackage(zipPath);

        Assert.Equal(3, signatureVerifier.VerifiedPaths.Count);
        Assert.Contains(signatureVerifier.VerifiedPaths, path =>
            path.EndsWith(Path.Combine("terminal-1.0", "WindowsTerminal.exe"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(signatureVerifier.VerifiedPaths, path =>
            path.EndsWith(Path.Combine("terminal-1.0", "Microsoft.Terminal.Control.dll"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(signatureVerifier.VerifiedPaths, path =>
            path.EndsWith(Path.Combine("terminal-1.0", "native-code.bin"), StringComparison.OrdinalIgnoreCase));
        Assert.All(signatureVerifier.VerifiedPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void VerifyPackage_WhenWindowsTerminalExecutableIsMissing_ThrowsBeforeSignatureVerification()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(zipPath, TextEntry("terminal-1.0/resources.pri", "resources"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("WindowsTerminal.exe", exception.Message, StringComparison.Ordinal);
        Assert.Empty(signatureVerifier.VerifiedPaths);
    }

    [Fact]
    public void VerifyPackage_WhenZipHasUnsafeEntryPath_ThrowsBeforeSignatureVerification()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(
            zipPath,
            PeEntry("terminal-1.0/WindowsTerminal.exe"),
            TextEntry("terminal-1.0/../evil.txt", "evil"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(signatureVerifier.VerifiedPaths);
    }

    [Fact]
    public void VerifyPackage_WhenPayloadRootEntryIsAFile_ThrowsBeforeSignatureVerification()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(
            zipPath,
            TextEntry("terminal-1.0", "not a directory"),
            PeEntry("terminal-1.0/WindowsTerminal.exe"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("payload root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(signatureVerifier.VerifiedPaths);
    }

    [Fact]
    public void VerifyPackage_WhenVerificationWorkPathIsOutsideProgramData_ThrowsBeforeExtraction()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(zipPath, PeEntry("terminal-1.0/WindowsTerminal.exe"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path) { IsUnderRootResult = false };
        var verifier = CreateVerifier(signatureVerifier, securityService);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("outside managed ProgramData", exception.Message, StringComparison.Ordinal);
        Assert.Empty(signatureVerifier.VerifiedPaths);
        Assert.Empty(securityService.CreatedDirectories);
    }

    [Fact]
    public void VerifyPackage_WhenExecutableExtensionEntryIsEmpty_ThrowsBeforeSignatureVerification()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(
            zipPath,
            PeEntry("terminal-1.0/WindowsTerminal.exe"),
            ("terminal-1.0/empty.dll", []));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("invalid executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyPackage_WhenExecutableExtensionEntryIsNotPortableExecutable_ThrowsBeforeSignatureVerification()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(
            zipPath,
            PeEntry("terminal-1.0/WindowsTerminal.exe"),
            TextEntry("terminal-1.0/not-a-pe.dll", "not a PE file"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("invalid executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyPackage_WhenExecutableSignatureVerifierRejects_PropagatesFailure()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(zipPath, PeEntry("terminal-1.0/WindowsTerminal.exe"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier { RejectAll = true };
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(signatureVerifier.VerifiedPaths);
    }

    [Fact]
    public void VerifyPackage_WhenPayloadDllSignatureVerifierRejects_PropagatesFailure()
    {
        var zipPath = Path.Combine(_tempDirectory.Path, "terminal.zip");
        CreateZip(
            zipPath,
            PeEntry("terminal-1.0/WindowsTerminal.exe"),
            PeEntry("terminal-1.0/Microsoft.Terminal.Control.dll"));
        var signatureVerifier = new FakeWindowsTerminalExecutableSignatureVerifier();
        signatureVerifier.RejectFileName("Microsoft.Terminal.Control.dll");
        var verifier = CreateVerifier(signatureVerifier);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.VerifyPackage(zipPath));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(signatureVerifier.VerifiedPaths, path =>
            path.EndsWith(Path.Combine("terminal-1.0", "WindowsTerminal.exe"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(signatureVerifier.VerifiedPaths, path =>
            path.EndsWith(Path.Combine("terminal-1.0", "Microsoft.Terminal.Control.dll"), StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private static (string EntryName, byte[] Content) PeEntry(string entryName)
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        bytes[60] = 64;
        bytes[64] = (byte)'P';
        bytes[65] = (byte)'E';
        return (entryName, bytes);
    }

    private static (string EntryName, byte[] Content) TextEntry(string entryName, string content)
        => (entryName, System.Text.Encoding.UTF8.GetBytes(content));

    private static void CreateZip(string zipPath, params (string EntryName, byte[] Content)[] entries)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var zipEntry = archive.CreateEntry(entry.EntryName);
            using var stream = zipEntry.Open();
            stream.Write(entry.Content);
        }
    }

    private WindowsTerminalPackageVerifier CreateVerifier(
        IWindowsTerminalExecutableSignatureVerifier signatureVerifier,
        TestProgramDataDirectorySecurityService? securityService = null)
    {
        var programDataService = securityService ?? new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        return new(
            programDataService,
            programDataService,
            new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(_tempDirectory.Path)),
            signatureVerifier,
            new WindowsTerminalPayloadFileInspector(),
            new WindowsTerminalDeploymentDirectoryCleaner());
    }

    private sealed class TestProgramDataDirectorySecurityService(string rootPath)
        : IProgramDataDirectoryProvisioningService, IProgramDataPathPolicyService
    {
        public List<string> CreatedDirectories { get; } = [];
        public bool IsUnderRootResult { get; init; } = true;

        public string EnsureRoot() => throw new NotSupportedException();

        public string EnsureSubdirectory(string relativePath, ProgramDataDirectoryAclProfile aclProfile)
            => throw new NotSupportedException();

        public string EnsureKnownDirectory(ProgramDataDirectoryPolicy policy)
        {
            var path = Path.Combine(rootPath, policy.RelativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void EnsureKnownDirectoryTreeInheritsFromRoot(ProgramDataDirectoryPolicy policy)
            => throw new NotSupportedException();

        public void EnsureDirectoryUnderRoot(string directoryPath, ProgramDataDirectoryAclProfile aclProfile)
        {
            CreatedDirectories.Add(directoryPath);
            Directory.CreateDirectory(directoryPath);
        }

        public void EnsureDirectoryTreeInheritsFromRoot(
            string directoryPath,
            ProgramDataDirectoryAclProfile rootAclProfile)
            => throw new NotSupportedException();

        public void EnsureTraverseOnlyAccess(
            string directoryPath,
            string sid,
            ProgramDataDirectoryAclProfile aclProfile)
            => throw new NotSupportedException();

        public bool IsUnderRoot(string path) => IsUnderRootResult;
    }

    private sealed class FakeWindowsTerminalExecutableSignatureVerifier : IWindowsTerminalExecutableSignatureVerifier
    {
        private readonly HashSet<string> rejectedFileNames = new(StringComparer.OrdinalIgnoreCase);

        public List<string> VerifiedPaths { get; } = [];
        public bool RejectAll { get; init; }

        public void RejectFileName(string fileName) => rejectedFileNames.Add(fileName);

        public void VerifyMicrosoftSignedExecutable(string executablePath)
        {
            VerifiedPaths.Add(executablePath);
            if (RejectAll || rejectedFileNames.Contains(Path.GetFileName(executablePath)))
                throw new InvalidOperationException("Executable signature verification failed.");
        }
    }
}

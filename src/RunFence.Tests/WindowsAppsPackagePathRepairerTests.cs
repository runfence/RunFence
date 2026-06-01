using RunFence.Launch;
using RunFence.Infrastructure;
using Moq;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsPackagePathRepairerTests
{
    [Fact]
    public void TryRepair_StalePackageVersion_UsesNewestMatchingInstalledPackageWithSameRelativeExecutable()
    {
        var stalePath = NotepadPath("11.2510.14.0");
        var repairedPath = NotepadPath("11.2512.1.0");
        var fileSystem = new TestBackupIntentFileSystem(
            [PackageDirectory("11.2510.14.0"), PackageDirectory("11.2512.1.0")],
            [repairedPath]);

        var result = new WindowsAppsPackagePathRepairer(fileSystem).TryRepair(stalePath);

        Assert.Equal(repairedPath, result);
    }

    [Fact]
    public void TryRepair_MultipleMatchingPackages_ChoosesHighestNumericVersion()
    {
        var stalePath = VendorPath("11.2.0.0", "x64", "publisher");
        var lowerLexicographicPath = VendorPath("11.9.0.0", "x64", "publisher");
        var higherNumericPath = VendorPath("11.10.0.0", "x64", "publisher");
        var fileSystem = new TestBackupIntentFileSystem(
            [
                VendorDirectory("11.9.0.0", "x64", "publisher"),
                VendorDirectory("11.10.0.0", "x64", "publisher"),
            ],
            [lowerLexicographicPath, higherNumericPath]);

        var result = new WindowsAppsPackagePathRepairer(fileSystem).TryRepair(stalePath);

        Assert.Equal(higherNumericPath, result);
    }

    [Fact]
    public void TryRepair_NoCandidateWithSameRelativeExecutable_ReturnsNull()
    {
        var stalePath = NotepadPath("11.2510.14.0");
        var fileSystem = new TestBackupIntentFileSystem(
            [PackageDirectory("11.2512.1.0")],
            [Path.Combine(PackageDirectory("11.2512.1.0"), "Other", "Notepad.exe")]);

        var result = new WindowsAppsPackagePathRepairer(fileSystem).TryRepair(stalePath);

        Assert.Null(result);
    }

    [Fact]
    public void TryRepair_FailedPackageDirectoryEnumeration_ReturnsNull()
    {
        var stalePath = NotepadPath("11.2510.14.0");
        var fileSystem = new TestBackupIntentFileSystem([], [], failEnumerationPaths: [@"C:\Program Files\WindowsApps"]);

        var result = new WindowsAppsPackagePathRepairer(fileSystem).TryRepair(stalePath);

        Assert.Null(result);
    }

    [Fact]
    public void TryRepair_UnknownCandidateExecutableTarget_IsSkipped()
    {
        var stalePath = NotepadPath("11.2510.14.0");
        var candidatePath = NotepadPath("11.2512.1.0");
        var fileSystem = new TestBackupIntentFileSystem(
            [PackageDirectory("11.2512.1.0")],
            [],
            unknownFiles: [candidatePath]);

        var result = new WindowsAppsPackagePathRepairer(fileSystem).TryRepair(stalePath);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("x86", "8wekyb3d8bbwe")]
    [InlineData("x64", "otherpublisher")]
    public void TryRepair_DifferentArchitectureOrPublisher_ReturnsNull(string architecture, string publisher)
    {
        var stalePath = NotepadPath("11.2510.14.0");
        var candidateDirectory = PackageDirectory("11.2512.1.0", architecture, publisher);
        var fileSystem = new TestBackupIntentFileSystem(
            [candidateDirectory],
            [Path.Combine(candidateDirectory, "Notepad", "Notepad.exe")]);

        var result = new WindowsAppsPackagePathRepairer(fileSystem).TryRepair(stalePath);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_RootedMissingExecutableWithoutRepair_ReturnsInvalid()
    {
        var fileSystem = new TestBackupIntentFileSystem([], []);
        var packageRepairer = new WindowsAppsPackagePathRepairer(fileSystem);
        var resolver = new AssociationExecutablePathResolver(fileSystem, packageRepairer);

        var result = resolver.Resolve(@"C:\Missing\tool.exe");

        Assert.False(result.IsValid);
        Assert.Equal(@"C:\Missing\tool.exe", result.ExePath);
    }

    [Fact]
    public void Resolve_UnknownExecutableState_DoesNotCallRepairAndReturnsInvalid()
    {
        var packageRepairer = new Mock<IWindowsAppsPackagePathRepairer>(MockBehavior.Strict);
        var fileSystem = new TestBackupIntentFileSystem([], [], unknownFiles: [@"C:\Program Files\WindowsApps\wt.exe"]);
        var resolver = new AssociationExecutablePathResolver(fileSystem, packageRepairer.Object);

        var result = resolver.Resolve(@"C:\Program Files\WindowsApps\wt.exe");

        Assert.False(result.IsValid);
        Assert.Contains("inaccessible", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
        packageRepairer.VerifyNoOtherCalls();
    }

    [Fact]
    public void Resolve_InvalidRootedExecutablePath_DoesNotCallRepairAndReturnsInvalid()
    {
        var packageRepairer = new Mock<IWindowsAppsPackagePathRepairer>(MockBehavior.Strict);
        var fileSystem = new BackupIntentFileSystem(new BackupIntentNativeFileSystem(), new BackupIntentManagedFileSystemProbe());
        var resolver = new AssociationExecutablePathResolver(fileSystem, packageRepairer.Object);

        var result = resolver.Resolve("C:\\bad\0tool.exe");

        Assert.False(result.IsValid);
        Assert.Contains("inaccessible", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
        packageRepairer.VerifyNoOtherCalls();
    }

    [Fact]
    public void Resolve_MissingExecutable_CallsRepairExactlyOnceAndReturnsRepairedPath()
    {
        const string stalePath = @"C:\Missing\tool.exe";
        const string repairedPath = @"C:\Repaired\tool.exe";
        var fileSystem = new Mock<IBackupIntentFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(current => current.GetFileState(stalePath)).Returns(BackupIntentPathState.Missing);

        var packageRepairer = new Mock<IWindowsAppsPackagePathRepairer>(MockBehavior.Strict);
        packageRepairer.Setup(current => current.TryRepair(stalePath)).Returns(repairedPath);

        var resolver = new AssociationExecutablePathResolver(fileSystem.Object, packageRepairer.Object);

        var result = resolver.Resolve(stalePath);

        Assert.True(result.IsValid);
        Assert.True(result.WasRepaired);
        Assert.Equal(repairedPath, result.ExePath);
        fileSystem.Verify(current => current.GetFileState(stalePath), Times.Once);
        packageRepairer.Verify(current => current.TryRepair(stalePath), Times.Once);
        packageRepairer.VerifyNoOtherCalls();
    }

    [Fact]
    public void Resolve_RootedMissingPackageExecutableWithRepair_ReturnsRepairedPath()
    {
        var stalePath = NotepadPath("11.2510.14.0");
        var repairedPath = NotepadPath("11.2512.1.0");
        var fileSystem = new TestBackupIntentFileSystem(
            [PackageDirectory("11.2512.1.0")],
            [repairedPath]);
        var packageRepairer = new WindowsAppsPackagePathRepairer(fileSystem);
        var resolver = new AssociationExecutablePathResolver(fileSystem, packageRepairer);

        var result = resolver.Resolve(stalePath);

        Assert.True(result.IsValid);
        Assert.True(result.WasRepaired);
        Assert.Equal(repairedPath, result.ExePath);
    }

    private static string NotepadPath(string version) =>
        Path.Combine(PackageDirectory(version), "Notepad", "Notepad.exe");

    private static string PackageDirectory(
        string version,
        string architecture = "x64",
        string publisher = "8wekyb3d8bbwe") =>
        Path.Combine(
            @"C:\Program Files\WindowsApps",
            $"Microsoft.WindowsNotepad_{version}_{architecture}__{publisher}");

    private static string VendorPath(string version, string architecture, string publisher) =>
        Path.Combine(VendorDirectory(version, architecture, publisher), "Tool", "Tool.exe");

    private static string VendorDirectory(string version, string architecture, string publisher) =>
        Path.Combine(@"C:\Program Files\WindowsApps", $"Vendor.Tool_{version}_{architecture}__{publisher}");

    private sealed class TestBackupIntentFileSystem(
        IEnumerable<string> directories,
        IEnumerable<string> files,
        IEnumerable<string>? unknownFiles = null,
        IEnumerable<string>? failEnumerationPaths = null) : IBackupIntentFileSystem
    {
        private readonly HashSet<string> _directories = new(directories, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files = new(files, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unknownFiles = new(unknownFiles ?? [], StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failEnumerationPaths = new(failEnumerationPaths ?? [], StringComparer.OrdinalIgnoreCase);

        public BackupIntentPathState GetFileState(string path)
        {
            if (_unknownFiles.Contains(path))
                return BackupIntentPathState.Unknown;

            return _files.Contains(path) ? BackupIntentPathState.Exists : BackupIntentPathState.Missing;
        }

        public BackupIntentPathState GetDirectoryState(string path)
            => _directories.Contains(path) ? BackupIntentPathState.Exists : BackupIntentPathState.Missing;

        public bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories)
        {
            if (_failEnumerationPaths.Contains(path))
            {
                directories = [];
                return false;
            }

            directories = _directories.Where(directory =>
                    string.Equals(Path.GetDirectoryName(directory), path, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return true;
        }

        public bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc)
        {
            lastWriteTimeUtc = default;
            return false;
        }
    }
}

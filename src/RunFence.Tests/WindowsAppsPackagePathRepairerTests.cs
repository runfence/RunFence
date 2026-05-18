using RunFence.Launch;
using RunFence.Infrastructure;
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

    [Fact]
    public void BackupIntentFileSystem_UsesNativeOpenForFilesAndDirectoryEnumeration()
    {
        var root = Path.Combine(Path.GetTempPath(), "RunFencePackageFsTests", Guid.NewGuid().ToString("N"));
        var childDirectory = Path.Combine(root, "ChildPackage_1.0.0.0_x64__publisher");
        var filePath = Path.Combine(root, "tool.exe");
        Directory.CreateDirectory(childDirectory);
        File.WriteAllText(filePath, string.Empty);
        try
        {
            var fileSystem = new BackupIntentFileSystem();

            Assert.True(fileSystem.FileExists(filePath));
            Assert.False(fileSystem.FileExists(Path.Combine(root, "missing.exe")));
            Assert.Contains(
                childDirectory,
                fileSystem.EnumerateDirectories(root),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        IEnumerable<string> files) : IBackupIntentFileSystem
    {
        private readonly HashSet<string> _directories = new(directories, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files = new(files, StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => _files.Contains(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _directories.Where(directory =>
                    string.Equals(Path.GetDirectoryName(directory), path, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }
}

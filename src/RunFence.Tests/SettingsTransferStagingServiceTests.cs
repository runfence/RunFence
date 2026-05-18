using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class SettingsTransferStagingServiceTests
{
    private static readonly string SharedTempRoot = Path.Combine(PathConstants.ProgramDataDir, "temp");
    private readonly SettingsTransferStagingService _service = new();

    [Fact]
    public void CreateSharedTempFilePath_CreatesPathInsideUniqueSharedTempDirectory()
    {
        var path = _service.CreateSharedTempFilePath(".json");

        Assert.StartsWith(SharedTempRoot, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".json", path, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(SharedTempRoot, Path.GetDirectoryName(path));
    }

    [Fact]
    public void CreateSharedTempDirectoryPath_ReturnsUniqueSharedTempDirectoryPath()
    {
        var path = _service.CreateSharedTempDirectoryPath();

        Assert.StartsWith(SharedTempRoot, path, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(SharedTempRoot, path);
    }

    [Fact]
    public void CopyImportFileToRestrictedTemp_CreatesRestrictedParentDirectoryAndPreservesContent()
    {
        using var sourceDir = new TempDirectory("SettingsTransferStagingTestsSource");
        var sourceFile = Path.Combine(sourceDir.Path, "source.json");
        File.WriteAllText(sourceFile, "{\"ok\":true}");

        var destination = _service.CreateSharedTempFilePath("json");
        var destinationDir = Path.GetDirectoryName(destination)!;

        var returned = _service.CopyImportFileToRestrictedTemp(
            sourceFile,
            destination,
            "S-1-5-21-2222-2222-2222-2222");

        Assert.Equal(destination, returned);
        Assert.Equal("{\"ok\":true}", File.ReadAllText(destination));
        Assert.True(Directory.Exists(destinationDir));

        _service.TryDeleteTempDirectory(destinationDir);
    }

    [Fact]
    public void CreateRestrictedExportDirectory_CreatesDirectory()
    {
        var path = _service.CreateSharedTempDirectoryPath();
        var created = _service.CreateRestrictedExportDirectory(path, "S-1-5-21-3333-3333-3333-3333");

        Assert.Equal(Path.GetFullPath(path), created);
        Assert.True(Directory.Exists(created));
    }

    [Fact]
    public void TryDeleteTempFile_AndDirectory_RemovePaths()
    {
        var directory = _service.CreateSharedTempDirectoryPath();
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "temp.json");
        File.WriteAllText(file, "x");

        var deletedFile = _service.TryDeleteTempFile(file);
        var deletedDir = _service.TryDeleteTempDirectory(directory);

        Assert.Equal(file, deletedFile);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(directory, deletedDir);
    }

    [Fact]
    public void BuildRestrictedDirectorySecurity_ContainsExpectedIdentities()
    {
        const string targetSid = "S-1-5-21-4444-4444-4444-4444";
        var security = _service.BuildRestrictedDirectorySecurity(targetSid);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

        var fileSystemRules = rules.Cast<FileSystemAccessRule>().ToList();
        var identityReferences = fileSystemRules
            .Select(rule => rule.IdentityReference.Value)
            .ToList();

        var currentSid = WindowsIdentity.GetCurrent().User?.Value;

        Assert.Contains("S-1-5-32-544", identityReferences);
        Assert.NotNull(currentSid);
        Assert.Contains(currentSid, identityReferences);
        Assert.Contains(fileSystemRules, rule =>
            rule.IdentityReference.Value == targetSid &&
            rule.FileSystemRights.HasFlag(FileSystemRights.Modify) &&
            rule.AccessControlType == AccessControlType.Allow);
    }

    [Fact]
    public void BuildRestrictedFileSecurity_ContainsTargetAccess()
    {
        var accountSid = "S-1-5-21-5555-5555-5555-5555";
        var security = _service.BuildRestrictedFileSecurity(accountSid);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

        Assert.Contains(rules.Cast<FileSystemAccessRule>(), rule =>
            rule.IdentityReference.Value == accountSid &&
            rule.FileSystemRights.HasFlag(FileSystemRights.Read) &&
            rule.AccessControlType == AccessControlType.Allow);
    }

    [Fact]
    public void TryDeleteTempFile_ReturnsNullWhenPathMissing()
    {
        var missingFile = Path.Combine(_service.CreateSharedTempDirectoryPath(), "missing.json");

        var deleted = _service.TryDeleteTempFile(missingFile);

        Assert.Null(deleted);
    }

    [Fact]
    public void CopyExportFileToDestination_CopiesFileToRequestedPath()
    {
        using var sourceDir = new TempDirectory("SettingsTransferExportSource");
        using var destinationDir = new TempDirectory("SettingsTransferExportDest");
        var sourcePath = Path.Combine(sourceDir.Path, "settings.json");
        var destinationPath = Path.Combine(destinationDir.Path, "copied.json");
        File.WriteAllText(sourcePath, "{\"copied\":true}");

        _service.CopyExportFileToDestination(sourcePath, destinationPath);

        Assert.Equal("{\"copied\":true}", File.ReadAllText(destinationPath));
    }
}

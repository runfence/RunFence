using RunFence.Account.UI;
using RunFence.Core;
using Moq;
using Xunit;

namespace RunFence.Tests;

public class PackageInstallScriptStoreTests : IDisposable
{
    private const string TestSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";
    private readonly List<string> _pathsToDelete = [];

    [Fact]
    public void CreateScript_CreatesScriptInProgramDataWithCommandContents()
    {
        var store = new PackageInstallScriptStore(Mock.Of<ILoggingService>());

        var path = store.CreateScript("Write-Host 'hello'", TestSid);
        _pathsToDelete.Add(path);

        Assert.StartsWith(PathConstants.ProgramDataDir, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".ps1", path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Write-Host 'hello'", File.ReadAllText(path));
    }

    [Fact]
    public void CleanupStaleScripts_RemovesOnlyOldInstallScripts()
    {
        Directory.CreateDirectory(PathConstants.ProgramDataDir);
        var stalePath = Path.Combine(PathConstants.ProgramDataDir, $"install-{Guid.NewGuid():N}.ps1");
        var freshPath = Path.Combine(PathConstants.ProgramDataDir, $"install-{Guid.NewGuid():N}.ps1");
        var otherPath = Path.Combine(PathConstants.ProgramDataDir, $"notes-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(stalePath, "stale");
        File.WriteAllText(freshPath, "fresh");
        File.WriteAllText(otherPath, "other");
        File.SetCreationTimeUtc(stalePath, DateTime.UtcNow.AddHours(-2));
        File.SetCreationTimeUtc(freshPath, DateTime.UtcNow);
        File.SetCreationTimeUtc(otherPath, DateTime.UtcNow.AddHours(-2));
        _pathsToDelete.AddRange([stalePath, freshPath, otherPath]);

        var store = new PackageInstallScriptStore(Mock.Of<ILoggingService>());

        store.CleanupStaleScripts();

        Assert.False(File.Exists(stalePath));
        Assert.True(File.Exists(freshPath));
        Assert.True(File.Exists(otherPath));
    }

    public void Dispose()
    {
        foreach (var path in _pathsToDelete)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}

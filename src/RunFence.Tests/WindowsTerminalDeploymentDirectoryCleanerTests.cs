using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalDeploymentDirectoryCleanerTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalDeploymentCleaner");

    [Fact]
    public void TryDeleteIfExists_BrokenJunction_DeletesJunction()
    {
        var targetPath = Path.Combine(_tempDirectory.Path, "target");
        var junctionPath = Path.Combine(_tempDirectory.Path, "stale-junction");
        Directory.CreateDirectory(targetPath);
        JunctionHelper.CreateJunction(junctionPath, targetPath);
        Directory.Delete(targetPath);
        Assert.Contains(junctionPath, Directory.EnumerateFileSystemEntries(_tempDirectory.Path));

        new WindowsTerminalDeploymentDirectoryCleaner().TryDeleteIfExists(junctionPath);

        Assert.DoesNotContain(junctionPath, Directory.EnumerateFileSystemEntries(_tempDirectory.Path));
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }
}

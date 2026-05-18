using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsAliasPathResolverTests
{
    [Fact]
    public void TryResolveForUserSid_AliasProbeAccessDenied_ReturnsNull()
    {
        var resolver = new WindowsAppsAliasPathResolver(
            new ThrowingExecutableFileSystem(),
            new FixedProfilePathReader(@"C:\Users\Target"));

        var resolved = resolver.TryResolveForUserSid("notepad.exe", "S-1-5-21-test");

        Assert.Null(resolved);
    }

    private sealed class ThrowingExecutableFileSystem : IExecutableFileSystem
    {
        public bool FileExists(string path) => true;

        public FileAttributes GetAttributes(string path) => throw new UnauthorizedAccessException("denied");
    }

    private sealed class FixedProfilePathReader(string profilePath) : IProfilePathReader
    {
        public string? GetProfilePath(string sid) => profilePath;
    }
}

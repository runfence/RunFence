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
            new FixedProfilePathReader(@"C:\Users\Target"),
            new FakeAppExecLinkReader());

        var resolved = resolver.TryResolveForUserSid("notepad.exe", "S-1-5-21-test");

        Assert.Null(resolved);
    }

    [Fact]
    public void IsWindowsAppsAliasPath_ReparsePoint_UsesAppExecLinkReader()
    {
        var appExecLinkReader = new FakeAppExecLinkReader
        {
            IsAppExecLinkResult = true,
        };
        var resolver = new WindowsAppsAliasPathResolver(
            new FixedExecutableFileSystem(FileAttributes.ReparsePoint),
            new FixedProfilePathReader(@"C:\Users\Target"),
            appExecLinkReader);

        var resolved = resolver.IsWindowsAppsAliasPath(@"C:\Users\Target\AppData\Local\Microsoft\WindowsApps\wt.exe");

        Assert.True(resolved);
        Assert.Equal(
            [@"C:\Users\Target\AppData\Local\Microsoft\WindowsApps\wt.exe"],
            appExecLinkReader.IsAppExecLinkCalls);
    }

    [Fact]
    public void IsWindowsAppsAliasPath_NonReparsePoint_DoesNotReadAppExecLink()
    {
        var appExecLinkReader = new FakeAppExecLinkReader();
        var resolver = new WindowsAppsAliasPathResolver(
            new FixedExecutableFileSystem(FileAttributes.Normal),
            new FixedProfilePathReader(@"C:\Users\Target"),
            appExecLinkReader);

        var resolved = resolver.IsWindowsAppsAliasPath(@"C:\Users\Target\AppData\Local\Microsoft\WindowsApps\wt.exe");

        Assert.False(resolved);
        Assert.Empty(appExecLinkReader.IsAppExecLinkCalls);
    }

    private sealed class ThrowingExecutableFileSystem : IExecutableFileSystem
    {
        public bool FileExists(string path) => true;

        public FileAttributes GetAttributes(string path) => throw new UnauthorizedAccessException("denied");
    }

    private sealed class FixedExecutableFileSystem(FileAttributes attributes) : IExecutableFileSystem
    {
        public bool FileExists(string path) => true;

        public FileAttributes GetAttributes(string path) => attributes;
    }

    private sealed class FixedProfilePathReader(string profilePath) : IProfilePathReader
    {
        public string? GetProfilePath(string sid) => profilePath;
    }

    private sealed class FakeAppExecLinkReader : IAppExecLinkReader
    {
        public bool IsAppExecLinkResult { get; init; }

        public List<string> IsAppExecLinkCalls { get; } = [];

        public bool IsAppExecLink(string path)
        {
            IsAppExecLinkCalls.Add(path);
            return IsAppExecLinkResult;
        }

        public bool TryReadStrings(string path, out IReadOnlyList<string> strings)
        {
            strings = [];
            return false;
        }
    }
}

using RunFence.Launching.Environment;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class ExecutablePathResolverTests
{
    [Fact]
    public void TryResolvePath_TargetEnvironmentWithoutSid_ResolvesWindowsAppsAliasFromLocalAppData()
    {
        var windowsAppsPath = Path.Combine(@"C:\Users\Target\AppData\Local", "Microsoft", "WindowsApps", "wt.exe");
        var resolver = CreateResolver([windowsAppsPath]);
        var environment = new DictionaryEnvironmentVariableReader(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Target\AppData\Local",
        });

        var resolved = resolver.TryResolvePath("wt", ExecutablePathResolutionContext.TargetEnvironment(environment));

        Assert.Equal(windowsAppsPath, resolved);
    }

    [Fact]
    public void TryResolvePath_TargetEnvironmentWithoutSid_ResolvesWindowsAppsAliasFromUserProfileWhenLocalAppDataMissing()
    {
        var windowsAppsPath = Path.Combine(@"C:\Users\Target\AppData\Local", "Microsoft", "WindowsApps", "wt.exe");
        var resolver = CreateResolver([windowsAppsPath]);
        var environment = new DictionaryEnvironmentVariableReader(new Dictionary<string, string>
        {
            ["USERPROFILE"] = @"C:\Users\Target",
        });

        var resolved = resolver.TryResolvePath("wt.exe", ExecutablePathResolutionContext.TargetEnvironment(environment));

        Assert.Equal(windowsAppsPath, resolved);
    }

    private static ExecutablePathResolver CreateResolver(IEnumerable<string> existingFiles) =>
        new(new TestExecutableFileSystem(existingFiles), new TestProfilePathReader());

    private sealed class TestExecutableFileSystem(IEnumerable<string> existingFiles) : IExecutableFileSystem
    {
        private readonly HashSet<string> _existingFiles = new(existingFiles, StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => _existingFiles.Contains(path);

        public FileAttributes GetAttributes(string path) => FileAttributes.Normal;
    }

    private sealed class TestProfilePathReader : IProfilePathReader
    {
        public string? GetProfilePath(string sid) => null;
    }
}

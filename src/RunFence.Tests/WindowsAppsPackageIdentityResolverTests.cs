using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsPackageIdentityResolverTests
{
    [Fact]
    public void TryResolvePackageIdentity_DirectPackageRootPath_ReturnsFamilyAndFullName()
    {
        const string exePath = @"C:\Program Files\WindowsApps\Contoso.Terminal_1.2.3.4_x64__8wekyb3d8bbwe\tools\terminal.exe";
        var resolver = new WindowsAppsPackageIdentityResolver(
            new FakeWindowsAppsAliasPathResolver(),
            new FakeAppExecLinkReader());

        var resolved = resolver.TryResolvePackageIdentity(exePath, out var resolution);

        Assert.True(resolved);
        Assert.Equal(exePath, resolution.PackageExecutablePath);
        Assert.Equal("Contoso.Terminal_8wekyb3d8bbwe", resolution.PackageIdentity.PackageFamilyName);
        Assert.Equal("Contoso.Terminal_1.2.3.4_x64__8wekyb3d8bbwe", resolution.PackageIdentity.PackageFullName);
    }

    [Fact]
    public void TryResolvePackageIdentity_InvalidPath_ReturnsFalse()
    {
        var resolver = new WindowsAppsPackageIdentityResolver(
            new FakeWindowsAppsAliasPathResolver(),
            new FakeAppExecLinkReader());

        var resolved = resolver.TryResolvePackageIdentity("C:\\bad\0path.exe", out var resolution);

        Assert.False(resolved);
        Assert.Equal(default, resolution);
    }

    [Fact]
    public void TryResolvePackageIdentity_NonWindowsAppsPath_ReturnsFalse()
    {
        var resolver = new WindowsAppsPackageIdentityResolver(
            new FakeWindowsAppsAliasPathResolver(),
            new FakeAppExecLinkReader());

        var resolved = resolver.TryResolvePackageIdentity(@"C:\Tools\terminal.exe", out var resolution);

        Assert.False(resolved);
        Assert.Equal(default, resolution);
    }

    [Fact]
    public void TryResolvePackageIdentity_AliasRecursion_ResolvesNestedPackagePath()
    {
        const string firstAlias = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe";
        const string secondAlias = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\terminal.exe";
        const string packagePath = @"C:\Program Files\WindowsApps\Contoso.Terminal_1.2.3.4_x64__8wekyb3d8bbwe\terminal.exe";

        var aliasResolver = new FakeWindowsAppsAliasPathResolver(firstAlias, secondAlias);
        var appExecLinkReader = new FakeAppExecLinkReader();
        appExecLinkReader.AddStrings(firstAlias, secondAlias, @"C:\Tools\noop.exe");
        appExecLinkReader.AddStrings(secondAlias, packagePath);
        var resolver = new WindowsAppsPackageIdentityResolver(aliasResolver, appExecLinkReader);

        var resolved = resolver.TryResolvePackageIdentity(firstAlias, out var resolution);

        Assert.True(resolved);
        Assert.Equal(packagePath, resolution.PackageExecutablePath);
        Assert.Equal("Contoso.Terminal_8wekyb3d8bbwe", resolution.PackageIdentity.PackageFamilyName);
        Assert.Equal("Contoso.Terminal_1.2.3.4_x64__8wekyb3d8bbwe", resolution.PackageIdentity.PackageFullName);
        Assert.Equal(
            [firstAlias, secondAlias],
            appExecLinkReader.TryReadStringsCalls);
    }

    [Fact]
    public void TryResolvePackageIdentity_InvalidAliasPayload_ReturnsFalse()
    {
        const string aliasPath = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe";

        var resolver = new WindowsAppsPackageIdentityResolver(
            new FakeWindowsAppsAliasPathResolver(aliasPath),
            new FakeAppExecLinkReader
            {
                StringsByPath =
                {
                    [aliasPath] = ["C:\\bad\0path.exe", "not-a-package"]
                },
            });

        var resolved = resolver.TryResolvePackageIdentity(aliasPath, out var resolution);

        Assert.False(resolved);
        Assert.Equal(default, resolution);
    }

    [Fact]
    public void TryResolvePackageIdentity_AliasCycle_StopsAtVisitedPaths()
    {
        const string firstAlias = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe";
        const string secondAlias = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\terminal.exe";

        var appExecLinkReader = new FakeAppExecLinkReader();
        appExecLinkReader.AddStrings(firstAlias, secondAlias);
        appExecLinkReader.AddStrings(secondAlias, firstAlias);
        var resolver = new WindowsAppsPackageIdentityResolver(
            new FakeWindowsAppsAliasPathResolver(firstAlias, secondAlias),
            appExecLinkReader);

        var resolved = resolver.TryResolvePackageIdentity(firstAlias, out var resolution);

        Assert.False(resolved);
        Assert.Equal(default, resolution);
        Assert.Equal([firstAlias, secondAlias], appExecLinkReader.TryReadStringsCalls);
    }

    private sealed class FakeWindowsAppsAliasPathResolver(params string[] aliasPaths) : IWindowsAppsAliasPathResolver
    {
        private readonly HashSet<string> _aliasPaths = new(aliasPaths, StringComparer.OrdinalIgnoreCase);

        public string? TryResolveForUserSid(string nameOrPath, string targetUserSid) => null;

        public bool IsWindowsAppsAliasPath(string path) => _aliasPaths.Contains(path);
    }

    private sealed class FakeAppExecLinkReader : IAppExecLinkReader
    {
        public Dictionary<string, IReadOnlyList<string>> StringsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> TryReadStringsCalls { get; } = [];

        public bool IsAppExecLink(string path) => StringsByPath.ContainsKey(path);

        public bool TryReadStrings(string path, out IReadOnlyList<string> strings)
        {
            TryReadStringsCalls.Add(path);
            return StringsByPath.TryGetValue(path, out strings!);
        }

        public void AddStrings(string path, params string[] strings)
        {
            StringsByPath[path] = strings;
        }
    }
}

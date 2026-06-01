using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsAppDiscoveryServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "RunFence.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiscoverApps_IncludesRegisteredProgramFilesPackageWithoutShortcut()
    {
        var packageDirectory = CreatePackageDirectory(
            "Program Files",
            "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
            """
            <Package xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
              <Applications>
                <Application Id="App" Executable="App\App.exe">
                  <uap:VisualElements DisplayName="Contoso App" />
                </Application>
              </Applications>
            </Package>
            """,
            @"App\App.exe");
        var service = CreateService([
            new RegisteredAppxPackage(
                "Contoso.App_8wekyb3d8bbwe",
                "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
                packageDirectory)
        ]);

        var apps = service.DiscoverApps();

        var discovered = Assert.Single(apps);
        Assert.Equal(new DiscoveredApp("Contoso App", Path.Combine(packageDirectory, "App", "App.exe")), discovered);
    }

    [Fact]
    public void DiscoverApps_KeepsLatestVersionForSamePackageExecutable()
    {
        var olderDirectory = CreatePackageDirectory(
            "Program Files",
            "Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe",
            """
            <Package>
              <Applications>
                <Application Id="App" Executable="App\App.exe" DisplayName="Contoso App" />
              </Applications>
            </Package>
            """,
            @"App\App.exe");
        var latestDirectory = CreatePackageDirectory(
            "Program Files",
            "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
            """
            <Package>
              <Applications>
                <Application Id="App" Executable="App\App.exe" DisplayName="Contoso App" />
              </Applications>
            </Package>
            """,
            @"App\App.exe");
        var service = CreateService([
            new RegisteredAppxPackage("Contoso.App_8wekyb3d8bbwe", Path.GetFileName(olderDirectory), olderDirectory),
            new RegisteredAppxPackage("Contoso.App_8wekyb3d8bbwe", Path.GetFileName(latestDirectory), latestDirectory)
        ]);

        var apps = service.DiscoverApps();

        var discovered = Assert.Single(apps);
        Assert.Equal(Path.Combine(latestDirectory, "App", "App.exe"), discovered.TargetPath);
    }

    [Fact]
    public void DiscoverApps_IgnoresPackageOutsideProgramFilesRoots()
    {
        var packageDirectory = CreatePackageDirectory(
            "Elsewhere",
            "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
            """
            <Package>
              <Applications>
                <Application Id="App" Executable="App\App.exe" DisplayName="Contoso App" />
              </Applications>
            </Package>
            """,
            @"App\App.exe");
        var service = CreateService([
            new RegisteredAppxPackage(
                "Contoso.App_8wekyb3d8bbwe",
                "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
                packageDirectory)
        ]);

        var apps = service.DiscoverApps();

        Assert.Empty(apps);
    }

    [Fact]
    public void DiscoverApps_IgnoresPackageEntryWithEscapingExecutablePath()
    {
        var packageDirectory = CreatePackageDirectory(
            "Program Files",
            "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
            """
            <Package>
              <Applications>
                <Application Id="App" Executable="..\Outside\App.exe" DisplayName="Contoso App" />
              </Applications>
            </Package>
            """);
        var service = CreateService([
            new RegisteredAppxPackage(
                "Contoso.App_8wekyb3d8bbwe",
                "Contoso.App_2.0.0.0_x64__8wekyb3d8bbwe",
                packageDirectory)
        ]);

        var apps = service.DiscoverApps();

        Assert.Empty(apps);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private WindowsAppsAppDiscoveryService CreateService(IReadOnlyList<RegisteredAppxPackage> packages)
    {
        var pathProvider = new FakeProgramFilesPathProvider([Path.Combine(tempRoot, "Program Files")]);
        var queryService = new Mock<IAppxPackageQueryService>(MockBehavior.Strict);
        queryService.Setup(service => service.QueryPackages()).Returns(packages);
        return new WindowsAppsAppDiscoveryService(
            pathProvider,
            queryService.Object,
            new FileContentService());
    }

    private string CreatePackageDirectory(string rootName, string packageFolderName, string manifestXml, string? executableRelativePath = null)
    {
        var packageDirectory = Path.Combine(tempRoot, rootName, "WindowsApps", packageFolderName);
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "AppxManifest.xml"), manifestXml);

        if (!string.IsNullOrWhiteSpace(executableRelativePath))
        {
            var fullExecutablePath = Path.Combine(packageDirectory, executableRelativePath.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullExecutablePath)!);
            File.WriteAllBytes(fullExecutablePath, [0]);
        }

        return packageDirectory;
    }

    private sealed class FakeProgramFilesPathProvider(IReadOnlyList<string> roots) : IProgramFilesPathProvider
    {
        public IReadOnlyList<string> GetProgramFilesRoots() => roots;
    }
}

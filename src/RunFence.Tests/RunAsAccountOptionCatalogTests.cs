using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsAccountOptionCatalogTests
{
    private const string FilePath = @"C:\Apps\tool.exe";

    [Fact]
    public void Build_CredentialSource_CreatesCredentialOption()
    {
        var catalog = CreateCatalog(suggestsBasicPrivilegeLevel: true);
        var source = new CredentialRunAsOptionSource(
            ListIndex: 3,
            DisplayText: "User",
            Credential: new CredentialEntry { Sid = "S-1-5-21-user" });

        var result = Assert.Single(catalog.Build(
            [source],
            existingApps: [],
            filePath: FilePath,
            currentUserSid: "S-1-5-21-current",
            accountPrivilegeLevels: new Dictionary<string, PrivilegeLevel>
            {
                ["S-1-5-21-user"] = PrivilegeLevel.Basic
            }));

        Assert.Equal(3, result.ListIndex);
        var option = Assert.IsType<CredentialRunAsOption>(result.Option);
        Assert.Same(source.Credential, option.Credential);
        Assert.Equal("User", option.DisplayName);
        Assert.Equal(PrivilegeLevel.Basic, option.AccountPrivilegeLevel);
        Assert.True(option.SuggestsBasicPrivilegeLevel);
    }

    [Fact]
    public void Build_AppContainerSource_AttachesMatchingExistingApp()
    {
        var catalog = CreateCatalog();
        var existingApp = new AppEntry
        {
            AppContainerName = "rfn_browser",
            ExePath = FilePath
        };
        var source = new AppContainerRunAsOptionSource(
            ListIndex: 6,
            DisplayText: "Browser (container)",
            Container: new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" },
            ContainerSid: "S-1-15-2-100");

        var result = Assert.Single(catalog.Build(
            [source],
            [existingApp],
            FilePath,
            currentUserSid: null,
            accountPrivilegeLevels: null));

        var option = Assert.IsType<AppContainerRunAsOption>(result.Option);
        Assert.Equal(6, result.ListIndex);
        Assert.Same(existingApp, option.ExistingAppForSelection);
    }

    private static RunAsAccountOptionCatalog CreateCatalog(bool suggestsBasicPrivilegeLevel = false)
    {
        var executableKindService = new Mock<IExecutableKindService>();
        executableKindService
            .Setup(service => service.SuggestsBasicPrivilegeLevel(It.IsAny<string>()))
            .Returns(suggestsBasicPrivilegeLevel);
        return new RunAsAccountOptionCatalog(executableKindService.Object);
    }
}

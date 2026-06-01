using Moq;
using RunFence.Core.Models;
using RunFence.Licensing;
using RunFence.Startup;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class OptionsPanelDataLoaderTests
{
    [Fact]
    public async Task LoadSettingsAsync_ProjectsShowForegroundPrivilegeMarker()
    {
        var autoStartService = new Mock<IAutoStartService>();
        autoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);
        var licenseService = new Mock<ILicenseService>();
        licenseService.SetupGet(service => service.IsLicensed).Returns(true);
        var loader = new OptionsPanelDataLoader(autoStartService.Object, licenseService.Object);
        var settings = new AppSettings
        {
            ShowForegroundPrivilegeMarker = false,
            ShowForegroundPrivilegeMarkerWhenFullscreen = false
        };

        var (state, settingsChangedByLicense) = await loader.LoadSettingsAsync(settings);

        Assert.False(settingsChangedByLicense);
        Assert.False(state.ShowForegroundPrivilegeMarker);
        Assert.False(state.ShowForegroundPrivilegeMarkerWhenFullscreen);
    }
}
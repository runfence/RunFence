using Moq;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

[Collection("Clipboard")]
public class ContainerContextMenuHandlerTests
{
    [Fact]
    public void CopyContainerProfilePath_UsesInjectedPathProvider()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var shellHelper = new Mock<IShellHelper>();
            var clipboard = new Mock<IClipboardTextService>();
            var orchestrator = AccountContainerOrchestratorTestFactory.Create();
            var profileActions = new AppContainerProfileActions(
                AppContainerProviderTestDoubles.CreatePathProvider(@"D:\Containers"),
                clipboard.Object,
                shellHelper.Object);
            var handler = new ContainerContextMenuHandler(orchestrator, profileActions);
            var row = new ContainerRow(
                new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser" },
                "S-1-15-2-42");

            handler.CopyContainerProfilePath(row);

            clipboard.Verify(service => service.SetText(@"D:\Containers\ram_browser"), Times.Once);
        });
    }

    [Fact]
    public void OpenContainerProfileFolder_UsesInjectedPathProvider()
    {
        var shellHelper = new Mock<IShellHelper>();
        var orchestrator = AccountContainerOrchestratorTestFactory.Create();
        var profileActions = new AppContainerProfileActions(
            AppContainerProviderTestDoubles.CreatePathProvider(@"D:\Containers"),
            Mock.Of<IClipboardTextService>(),
            shellHelper.Object);
        var handler = new ContainerContextMenuHandler(orchestrator, profileActions);
        var row = new ContainerRow(
            new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser" },
            "S-1-15-2-42");

        handler.OpenContainerProfileFolder(row);

        shellHelper.Verify(s => s.OpenInExplorer(@"D:\Containers\ram_browser"), Times.Once);
    }
}

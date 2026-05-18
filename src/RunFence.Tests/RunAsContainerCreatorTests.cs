using System.Windows.Forms;
using Moq;
using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsContainerCreatorTests
{
    private sealed class FakeCreationUi : IRunAsContainerCreationUI
    {
        private readonly AppContainerEntry? _result;

        public FakeCreationUi(AppContainerEntry? result) => _result = result;

        public AppContainerEntry? ShowCreateContainerDialog()
            => _result;
    }

    [Fact]
    public void CreateNewContainer_ReturnsEntryAndNotifies()
    {
        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(s => s.Database).Returns(new AppDatabase());
        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var created = new AppContainerEntry { Name = "rfn_demo", DisplayName = "Demo" };
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(s => s.CanCreateContainer(It.IsAny<int>())).Returns(true);

        var creationUi = new FakeCreationUi(created);
        var creator = CreateCreator(appState.Object, dataChangeNotifier.Object, creationUi, messageBoxService.Object, licenseService.Object);

        var result = creator.CreateNewContainer();

        Assert.Same(created, result);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        messageBoxService.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateNewContainer_WhenCancelled_DoesNotNotify()
    {
        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(s => s.Database).Returns(new AppDatabase());
        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var creationUi = new FakeCreationUi(null);
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(s => s.CanCreateContainer(It.IsAny<int>())).Returns(true);

        var creator = CreateCreator(appState.Object, dataChangeNotifier.Object, creationUi, messageBoxService.Object, licenseService.Object);

        var result = creator.CreateNewContainer();

        Assert.Null(result);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Never);
        messageBoxService.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateNewContainer_WhenLicenseDenied_ShowsRestrictionAndSkipsDialog()
    {
        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(s => s.Database).Returns(new AppDatabase());
        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var creationUi = new FakeCreationUi(new AppContainerEntry { Name = "ignored", DisplayName = "Ignored" });
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(s => s.CanCreateContainer(It.IsAny<int>())).Returns(false);
        licenseService
            .Setup(s => s.GetRestrictionMessage(EvaluationFeature.Containers, It.IsAny<int>()))
            .Returns("limit");

        var creator = CreateCreator(appState.Object, dataChangeNotifier.Object, creationUi, messageBoxService.Object, licenseService.Object);

        var result = creator.CreateNewContainer();

        Assert.Null(result);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Never);
        messageBoxService.Verify(
            s => s.Show(
                null,
                "limit",
                "License Limit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1),
            Times.Once);
    }

    private static RunAsContainerCreator CreateCreator(
        IAppStateProvider appState,
        IDataChangeNotifier dataChangeNotifier,
        IRunAsContainerCreationUI creationUi,
        IAccountMessageBoxService messageBoxService,
        ILicenseService licenseService)
    {
        return new RunAsContainerCreator(
            appState,
            dataChangeNotifier,
            creationUi,
            messageBoxService,
            licenseService);
    }
}

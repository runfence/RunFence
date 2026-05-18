using RunFence.Account.UI.AppContainer;
using RunFence.Core.Models;
using Moq;
using Xunit;

namespace RunFence.Tests;

public class AppContainerDialogResultPresenterTests
{
    [Fact]
    public void ApplyResult_CreateAfterOsSaveFailure_StoresStateShowsWarningAndCloses()
    {
        var notifier = new Mock<IAppContainerEditDialogNotifier>();
        var presenter = new AppContainerDialogResultPresenter(notifier.Object);
        var createdEntry = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var context = new FakeResultContext(isCreateMode: true);

        var closeResult = presenter.ApplyResult(
            context,
            Mock.Of<IWin32Window>(),
            new AppContainerEditSubmitResult
            {
                DialogResult = DialogResult.OK,
                CreatedEntry = createdEntry,
                OperationStatus = AppContainerOperationStatus.SaveFailedAfterOs,
                PersistenceWarningText = "Failed to create container: final save failed"
            });

        Assert.Equal(DialogResult.OK, closeResult);
        Assert.Same(createdEntry, context.CreatedEntry);
        Assert.Equal(AppContainerOperationStatus.SaveFailedAfterOs, context.LastOperationStatus);
        Assert.Equal(AppContainerOperationStatus.SaveFailedAfterOs, context.PendingNotificationStatus);
        notifier.Verify(
            service => service.ShowPersistenceWarning(It.IsAny<IWin32Window>(), "Failed to create container: final save failed"),
            Times.Once);
    }

    [Fact]
    public void ApplyResult_ValidationFailure_KeepsDialogOpenAndShowsValidation()
    {
        var notifier = new Mock<IAppContainerEditDialogNotifier>();
        var presenter = new AppContainerDialogResultPresenter(notifier.Object);
        var context = new FakeResultContext(isCreateMode: true);

        var closeResult = presenter.ApplyResult(
            context,
            Mock.Of<IWin32Window>(),
            new AppContainerEditSubmitResult
            {
                DialogResult = DialogResult.None,
                ValidationMessage = "Display name is required."
            });

        Assert.Null(closeResult);
        Assert.Null(context.LastOperationStatus);
        notifier.Verify(
            service => service.ShowValidationWarning(It.IsAny<IWin32Window>(), "Display name is required."),
            Times.Once);
    }

    [Fact]
    public void ApplyResult_EditCapabilityChangeSuccess_ShowsRestartAndComWarnings()
    {
        var notifier = new Mock<IAppContainerEditDialogNotifier>();
        var presenter = new AppContainerDialogResultPresenter(notifier.Object);
        var context = new FakeResultContext(isCreateMode: false);

        var closeResult = presenter.ApplyResult(
            context,
            Mock.Of<IWin32Window>(),
            new AppContainerEditSubmitResult
            {
                DialogResult = DialogResult.OK,
                OperationStatus = AppContainerOperationStatus.Succeeded,
                RestartRequired = true,
                ComAccessWarnings = ["Grant {CLSID-1}: denied"]
            });

        Assert.Equal(DialogResult.OK, closeResult);
        Assert.Equal(AppContainerOperationStatus.Succeeded, context.LastOperationStatus);
        notifier.Verify(service => service.ShowRestartRequired(It.IsAny<IWin32Window>()), Times.Once);
        notifier.Verify(
            service => service.ShowComAccessWarning(
                It.IsAny<IWin32Window>(),
                It.Is<IReadOnlyList<string>>(warnings => warnings.Count == 1 && warnings[0] == "Grant {CLSID-1}: denied")),
            Times.Once);
    }

    private sealed class FakeResultContext(bool isCreateMode) : IAppContainerEditDialogResultContext
    {
        public bool IsCreateMode => isCreateMode;

        public string PendingValidationCaption { get; set; } = "Validation";

        public AppContainerOperationStatus? PendingNotificationStatus { get; set; }

        public AppContainerEntry? CreatedEntry { get; set; }

        public AppContainerOperationStatus? LastOperationStatus { get; set; }
    }
}

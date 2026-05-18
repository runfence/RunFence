using RunFence.Account.UI.AppContainer;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppContainerEditSubmitControllerTests
{
    [Fact]
    public async Task SubmitAsync_EmptyDisplayName_ReturnsValidationFailureWithoutCallingService()
    {
        var service = new FakeAppContainerEditService();
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            DisplayName = "   ",
            ProfileName = "rfn_browser",
        });

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Equal("Display name is required.", result.ValidationMessage);
        Assert.Equal(0, service.CreateCallCount);
        Assert.Equal(0, service.EditCallCount);
    }

    [Fact]
    public async Task SubmitAsync_CreateAfterOsSaveFailure_ReturnsCloseResultWithPersistenceWarning()
    {
        var createdEntry = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) => Task.FromResult(
                new AppContainerCreateResult(
                    AppContainerOperationStatus.SaveFailedAfterOs,
                    createdEntry,
                    "final save failed",
                    []))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            DisplayName = "Browser",
            ProfileName = "rfn_browser",
        });

        Assert.Equal(DialogResult.OK, result.DialogResult);
        Assert.Equal(AppContainerOperationStatus.SaveFailedAfterOs, result.OperationStatus);
        Assert.Same(createdEntry, result.CreatedEntry);
        Assert.Equal("Failed to create container: final save failed", result.PersistenceWarningText);
        Assert.Null(result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_EditCapabilityChangeSuccess_ReturnsRestartRequired()
    {
        var existing = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            ApplyEditChangesAsync = (_, _, _, _, _, _) => Task.FromResult(
                new AppContainerEditResult(
                    AppContainerOperationStatus.Succeeded,
                    existing,
                    CapabilitiesChanged: true,
                    null,
                    []))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            Existing = existing,
            DisplayName = "Browser",
            ProfileName = existing.Name,
            Capabilities = ["S-1-15-3-1"],
        });

        Assert.Equal(DialogResult.OK, result.DialogResult);
        Assert.Equal(AppContainerOperationStatus.Succeeded, result.OperationStatus);
        Assert.True(result.RestartRequired);
        Assert.Null(result.PersistenceWarningText);
        Assert.Null(result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_EditPreOsFailure_ReturnsBlockingOperationError()
    {
        var existing = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            ApplyEditChangesAsync = (_, _, _, _, _, _) => Task.FromResult(
                new AppContainerEditResult(
                    AppContainerOperationStatus.SaveFailedBeforeOs,
                    existing,
                    CapabilitiesChanged: true,
                    "save failed",
                    []))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            Existing = existing,
            DisplayName = "Browser",
            ProfileName = existing.Name,
        });

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Equal(AppContainerOperationStatus.SaveFailedBeforeOs, result.OperationStatus);
        Assert.False(result.RestartRequired);
        Assert.Equal("save failed", result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_CreateServiceThrows_ReturnsBlockingOperationError()
    {
        var controller = new AppContainerEditSubmitController(new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) => throw new InvalidOperationException("boom"),
        });

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            DisplayName = "Browser",
            ProfileName = "rfn_browser",
        });

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Equal("boom", result.OperationErrorText);
        Assert.Null(result.OperationStatus);
    }

    private sealed class FakeAppContainerEditService : IAppContainerEditService
    {
        public int CreateCallCount { get; private set; }
        public int EditCallCount { get; private set; }

        public Func<AppContainerEntry, string, List<string>, bool, List<string>, bool, Task<AppContainerEditResult>>? ApplyEditChangesAsync { get; init; }
        public Func<string, string, bool, List<string>, bool, List<string>, Task<AppContainerCreateResult>>? CreateNewContainerAsync { get; init; }

        public Task<AppContainerEditResult> ApplyEditChanges(
            AppContainerEntry existing,
            string displayName,
            List<string> capabilities,
            bool loopback,
            List<string> newComClsids,
            bool isEphemeral)
        {
            EditCallCount++;
            if (ApplyEditChangesAsync != null)
                return ApplyEditChangesAsync(existing, displayName, capabilities, loopback, newComClsids, isEphemeral);

            return Task.FromResult(new AppContainerEditResult(
                AppContainerOperationStatus.Succeeded,
                existing,
                false,
                null,
                []));
        }

        public Task<AppContainerCreateResult> CreateNewContainer(
            string profileName,
            string displayName,
            bool isEphemeral,
            List<string> capabilities,
            bool loopback,
            List<string> comClsids)
        {
            CreateCallCount++;
            if (CreateNewContainerAsync != null)
                return CreateNewContainerAsync(profileName, displayName, isEphemeral, capabilities, loopback, comClsids);

            return Task.FromResult(new AppContainerCreateResult(
                AppContainerOperationStatus.Succeeded,
                new AppContainerEntry { Name = profileName, DisplayName = displayName },
                null,
                []));
        }
    }
}

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
    }

    [Fact]
    public async Task SubmitAsync_CreateSuccess_ReturnsSuccessAndCreatedEntryWithoutWarningsOrErrors()
    {
        var createdEntry = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerCreateResult(
                AppContainerOperationStatus.Succeeded,
                createdEntry,
                null,
                ["Grant {CLSID-1}: granted"]))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            ProfileName = "rfn_browser",
            DisplayName = "Browser",
            Capabilities = ["S-1-15-3-1"],
        });

        Assert.Equal(DialogResult.OK, result.DialogResult);
        Assert.Same(createdEntry, result.CreatedEntry);
        Assert.Equal(AppContainerOperationStatus.Succeeded, result.OperationStatus);
        Assert.Equal(["Grant {CLSID-1}: granted"], result.ComAccessWarnings);
        Assert.Null(result.PersistenceWarningText);
        Assert.Null(result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_CreateSaveFailedAfterOs_ReturnsSuccessWithPersistenceWarning()
    {
        var createdEntry = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerCreateResult(
                AppContainerOperationStatus.SaveFailedAfterOs,
                createdEntry,
                "disk write failed",
                []))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            ProfileName = "rfn_browser",
            DisplayName = "Browser",
            Capabilities = ["S-1-15-3-1"],
        });

        Assert.Equal(DialogResult.OK, result.DialogResult);
        Assert.Same(createdEntry, result.CreatedEntry);
        Assert.Equal(AppContainerOperationStatus.SaveFailedAfterOs, result.OperationStatus);
        Assert.Equal("Failed to create container: disk write failed", result.PersistenceWarningText);
        Assert.Null(result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_CreateSaveFailedBeforeOs_ReturnsWarningAndNoCreatedEntry()
    {
        var service = new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerCreateResult(
                AppContainerOperationStatus.SaveFailedBeforeOs,
                null,
                "db write failed",
                []))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            ProfileName = "rfn_browser",
            DisplayName = "Browser",
            Capabilities = ["S-1-15-3-1"],
        });

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Null(result.CreatedEntry);
        Assert.Equal(AppContainerOperationStatus.SaveFailedBeforeOs, result.OperationStatus);
        Assert.Equal("Failed to create container: db write failed", result.PersistenceWarningText);
        Assert.Null(result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_CreateSystemFailed_ReturnsOperationError()
    {
        var service = new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerCreateResult(
                AppContainerOperationStatus.SystemFailed,
                null,
                "kernel panic",
                []))
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            ProfileName = "rfn_browser",
            DisplayName = "Browser",
            Capabilities = ["S-1-15-3-1"],
        });

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Equal(AppContainerOperationStatus.SystemFailed, result.OperationStatus);
        Assert.Equal("Failed to create container: kernel panic", result.OperationErrorText);
        Assert.Null(result.PersistenceWarningText);
    }

    [Fact]
    public async Task SubmitAsync_EditSaveFailedAfterOs_ReturnsCloseWithoutRestartAndPersistenceWarning()
    {
        var existing = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            ApplyEditChangesAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerEditResult(
                AppContainerOperationStatus.SaveFailedAfterOs,
                existing,
                CapabilitiesChanged: false,
                ErrorMessage: "post-os save failed",
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
        Assert.Null(result.CreatedEntry);
        Assert.Equal(AppContainerOperationStatus.SaveFailedAfterOs, result.OperationStatus);
        Assert.False(result.RestartRequired);
        Assert.Equal("post-os save failed", result.PersistenceWarningText);
        Assert.Null(result.OperationErrorText);
    }

    [Fact]
    public async Task SubmitAsync_EditSaveFailedAfterOs_WithCapabilitiesChanged_ReturnsRestartRequired()
    {
        var existing = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            ApplyEditChangesAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerEditResult(
                AppContainerOperationStatus.SaveFailedAfterOs,
                existing,
                CapabilitiesChanged: true,
                ErrorMessage: "post-os save failed",
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
        Assert.True(result.RestartRequired);
    }

    [Fact]
    public async Task SubmitAsync_EditSystemFailed_ReturnsOperationError()
    {
        var existing = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };
        var service = new FakeAppContainerEditService
        {
            ApplyEditChangesAsync = (_, _, _, _, _, _) => Task.FromResult(new AppContainerEditResult(
                AppContainerOperationStatus.SystemFailed,
                existing,
                CapabilitiesChanged: false,
                ErrorMessage: "edit failed",
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

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Equal(AppContainerOperationStatus.SystemFailed, result.OperationStatus);
        Assert.Equal("edit failed", result.OperationErrorText);
        Assert.Null(result.PersistenceWarningText);
    }

    [Fact]
    public async Task SubmitAsync_ServiceThrows_ReturnsExceptionMessageAsOperationError()
    {
        var service = new FakeAppContainerEditService
        {
            CreateNewContainerAsync = (_, _, _, _, _, _) =>
                throw new InvalidOperationException("creation failed")
        };
        var controller = new AppContainerEditSubmitController(service);

        var result = await controller.SubmitAsync(new AppContainerEditSubmitRequest
        {
            ProfileName = "rfn_browser",
            DisplayName = "Browser",
            Capabilities = ["S-1-15-3-1"],
        });

        Assert.Equal(DialogResult.None, result.DialogResult);
        Assert.Equal("creation failed", result.OperationErrorText);
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

using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerEditSubmitController(IAppContainerEditService editService)
{
    public async Task<AppContainerEditSubmitResult> SubmitAsync(AppContainerEditSubmitRequest request)
    {
        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrEmpty(displayName))
        {
            return new AppContainerEditSubmitResult
            {
                DialogResult = DialogResult.None,
                ValidationMessage = "Display name is required.",
            };
        }

        try
        {
            return request.Existing != null
                ? await SubmitEditAsync(request.Existing, displayName, request)
                : await SubmitCreateAsync(displayName, request);
        }
        catch (Exception ex)
        {
            return new AppContainerEditSubmitResult
            {
                DialogResult = DialogResult.None,
                OperationErrorText = ex.Message,
            };
        }
    }

    private async Task<AppContainerEditSubmitResult> SubmitEditAsync(
        AppContainerEntry existing,
        string displayName,
        AppContainerEditSubmitRequest request)
    {
        var result = await editService.ApplyEditChanges(
            existing,
            displayName,
            [.. request.Capabilities],
            request.LoopbackChecked,
            [.. request.ComClsids],
            request.IsEphemeral);

        var submitResult = new AppContainerEditSubmitResult
        {
            DialogResult = IsSuccessfulCloseStatus(result.Status) ? DialogResult.OK : DialogResult.None,
            OperationStatus = result.Status,
            RestartRequired = result.CapabilitiesChanged && IsSuccessfulCloseStatus(result.Status),
            ComAccessWarnings = result.Warnings,
        };

        if (result.ErrorMessage == null)
            return submitResult;

        return result.Status == AppContainerOperationStatus.SaveFailedAfterOs
            ? submitResult with { PersistenceWarningText = result.ErrorMessage }
            : submitResult with { OperationErrorText = result.ErrorMessage };
    }

    private async Task<AppContainerEditSubmitResult> SubmitCreateAsync(
        string displayName,
        AppContainerEditSubmitRequest request)
    {
        var createResult = await editService.CreateNewContainer(
            request.ProfileName,
            displayName,
            request.IsEphemeral,
            [.. request.Capabilities],
            request.LoopbackChecked,
            [.. request.ComClsids]);

        var submitResult = new AppContainerEditSubmitResult
        {
            DialogResult = IsSuccessfulCloseStatus(createResult.Status) ? DialogResult.OK : DialogResult.None,
            CreatedEntry = IsSuccessfulCloseStatus(createResult.Status) ? createResult.Entry : null,
            OperationStatus = createResult.Status,
            ComAccessWarnings = createResult.Warnings,
        };

        if (createResult.ErrorMessage != null)
        {
            var createErrorMessage = $"Failed to create container: {createResult.ErrorMessage}";
            return createResult.Status is AppContainerOperationStatus.SaveFailedBeforeOs or AppContainerOperationStatus.SaveFailedAfterOs
                ? submitResult with { PersistenceWarningText = createErrorMessage }
                : submitResult with { OperationErrorText = createErrorMessage };
        }

        if (IsSuccessfulCloseStatus(createResult.Status))
            return submitResult;

        return submitResult with { OperationErrorText = "Failed to create container." };
    }

    private static bool IsSuccessfulCloseStatus(AppContainerOperationStatus status)
        => status is AppContainerOperationStatus.Succeeded or AppContainerOperationStatus.SaveFailedAfterOs;
}

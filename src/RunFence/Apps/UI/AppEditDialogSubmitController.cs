using RunFence.Core.Models;
using System.Windows.Forms;

namespace RunFence.Apps.UI;

public class AppEditDialogSubmitController(
    AppEditDialogController controller,
    AppEditDialogSaveHandler saveHandler)
{
    public Task<AppEditDialogSubmitResult> SubmitAsync(AppEditDialogSubmitRequest request)
    {
        var buildResult = controller.ValidateAndBuild(request.Input);
        if (buildResult.Result == null)
        {
            return Task.FromResult(new AppEditDialogSubmitResult(
                DialogResult: null,
                Result: null,
                HasUnsavedMutations: false,
                StatusText: buildResult.StatusText));
        }

        return Task.FromResult(new AppEditDialogSubmitResult(
            DialogResult: null,
            Result: buildResult.Result,
            HasUnsavedMutations: false,
            StatusText: buildResult.StatusText));
    }

    public async Task<AppEditDialogSubmitResult> ApplyExistingResultAsync(AppEditDialogApplyRequest request)
    {
        var saveResult = await saveHandler.TrySaveAndApply(
            request.Result,
            request.SelectedConfigPath,
            request.Database,
            request.CurrentAssociations,
            request.ApplyAsync);

        return saveResult.Status switch
        {
            AppEditSaveStatus.Saved => new AppEditDialogSubmitResult(
                DialogResult: DialogResult.OK,
                Result: request.Result,
                HasUnsavedMutations: false),
            AppEditSaveStatus.SavedWithRegistryWarning => new AppEditDialogSubmitResult(
                DialogResult: DialogResult.OK,
                Result: request.Result,
                HasUnsavedMutations: false,
                NotificationMessage: $"Application was saved, but handler registration sync failed:\n\n{saveResult.RegistrySyncWarning}",
                NotificationIsWarning: true),
            AppEditSaveStatus.Canceled => new AppEditDialogSubmitResult(
                DialogResult: null,
                Result: request.Result,
                HasUnsavedMutations: false,
                StatusText: string.Empty),
            AppEditSaveStatus.SaveFailed => new AppEditDialogSubmitResult(
                DialogResult: null,
                Result: request.Result,
                HasUnsavedMutations: true,
                StatusText: $"Failed: {saveResult.SaveError}",
                StatusIsError: true),
            AppEditSaveStatus.ValidationOrSystemFailed => new AppEditDialogSubmitResult(
                DialogResult: null,
                Result: request.Result,
                HasUnsavedMutations: false,
                StatusText: $"Failed: {saveResult.SaveError}",
                StatusIsError: true),
            _ => throw new InvalidOperationException($"Unexpected save status '{saveResult.Status}'.")
        };
    }
}

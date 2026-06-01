using RunFence.Core.Models;
using RunFence.Persistence;
using System.Windows.Forms;

namespace RunFence.Apps.UI;

public class AppEditDialogSubmitController(
    AppEditDialogController controller,
    AppEditDialogSaveHandler saveHandler,
    AppEditAssociationHandler associationHandler,
    IAppConfigService appConfigService,
    AppEntryChangeClassifier changeClassifier)
{
    public AppEditDialogSubmitResult Submit(AppEditDialogSubmitRequest request)
    {
        var buildResult = controller.ValidateAndBuild(request.Input);
        if (buildResult.Result == null)
        {
            return new AppEditDialogSubmitResult(
                DialogResult: null,
                Result: null,
                HasUnsavedMutations: false,
                StatusText: buildResult.StatusText);
        }

        return new AppEditDialogSubmitResult(
            DialogResult: null,
            Result: buildResult.Result,
            HasUnsavedMutations: false,
            StatusText: buildResult.StatusText);
    }

    public async Task<AppEditDialogSubmitResult> ApplyExistingResultAsync(AppEditDialogApplyRequest request)
    {
        var previousApp = request.Database?.Apps
            .FirstOrDefault(app => string.Equals(app.Id, request.Result.Id, StringComparison.Ordinal))
            ?.Clone();
        var previousAssociations = request.Database != null
            ? associationHandler.GetCurrentAssociations(request.Result.Id) ?? []
            : [];
        var previousConfigPath = previousApp != null
            ? appConfigService.GetConfigPath(previousApp.Id)
            : null;
        var changeSet = previousApp != null
            ? changeClassifier.Classify(
                previousApp,
                request.Result,
                previousAssociations,
                request.CurrentAssociations,
                previousConfigPath,
                request.SelectedConfigPath)
            : new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: request.CurrentAssociations.Count > 0,
                RequiresManagedShortcutRefresh: false,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly);

        if (previousApp != null)
            AppEntryShortcutProtectionStateHelper.ApplyExistingEditState(previousApp, request.Result, changeSet);

        var applyContext = new AppEditDialogApplyContext(
            request.Result,
            previousApp,
            changeSet,
            previousConfigPath,
            request.SelectedConfigPath,
            request.CurrentAssociations);

        var saveResult = await saveHandler.TrySaveAndApply(
            request.Database,
            applyContext,
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

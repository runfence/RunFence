using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public class HandlerMappingDialogSubmissionCoordinator(
    HandlerMappingDialogHelper dialogHelper,
    HandlerMappingMutationHandler mutationHandler,
    HandlerMappingSubmitTransaction submitTransaction,
    ILoggingService log)
{
    private const string RegistrySyncFailurePrefix =
        "RunFence saved the handler association change, but registry synchronization failed:";
    private const string SaveFailurePrefix =
        "RunFence could not save handler associations:";

    public Task<HandlerMappingDialogSubmitResult> SubmitAddAsync(
        HandlerMappingAddDialogSubmitRequest request,
        IHandlerMappingDialogPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(persistence);

        return ExecuteRetryableAsync(async () =>
        {
            if (request.IsDirectMode)
            {
                var validation = dialogHelper.ValidateDirectHandler(request.ResolvedKeys, request.DirectHandlerValue);
                if (!validation.IsValid || validation.DirectHandlerEntries == null)
                    return ValidationFailure(validation.ErrorMessage);

                var result = await submitTransaction.SubmitAsync(
                    persistence,
                    validation.ValidKeys,
                    [],
                    database => mutationHandler.AddDirectHandler(database, validation.ValidKeys, validation.DirectHandlerEntries).KeysToRestore);
                return CreateSubmitResult(result);
            }

            var appValidation = dialogHelper.ValidateAppMapping(
                request.ResolvedKeys,
                request.SelectedApp,
                request.ArgumentsTemplate,
                request.AppPrefixes,
                request.PathPrefixes,
                request.ReplacePrefixes);
            if (!appValidation.IsValid || request.SelectedApp is not { } app)
                return ValidationFailure(appValidation.ErrorMessage);

            var appResult = await submitTransaction.SubmitAsync(
                persistence,
                appValidation.ValidKeys,
                [app.Id],
                database =>
                {
                    mutationHandler.UpdateAppPrefixes(database, app.Id, request.AppPrefixes);
                    mutationHandler.AddAppMapping(
                        database,
                        appValidation.ValidKeys,
                        app,
                        request.ArgumentsTemplate,
                        request.PathPrefixes,
                        request.ReplacePrefixes,
                        requiresAllowPassingArgumentsEnable: appValidation.RequiresAllowPassingArgumentsEnable);
                    return null;
                });
            return CreateSubmitResult(appResult);
        });
    }

    public Task<HandlerMappingDialogSubmitResult> SubmitEditAppAsync(
        EditAppHandlerMappingSubmitRequest request,
        IHandlerMappingDialogPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(persistence);

        return ExecuteRetryableAsync(async () =>
        {
            var validation = dialogHelper.ValidateAppMapping(
                [request.Key],
                request.SelectedApp,
                request.ArgumentsTemplate,
                request.AppPrefixes,
                request.PathPrefixes,
                request.ReplacePrefixes);
            if (!validation.IsValid || request.SelectedApp is not { } selectedApp)
                return ValidationFailure(validation.ErrorMessage);

            if (!HasEditAppMutation(request, selectedApp, validation))
                return Close();

            var result = await submitTransaction.SubmitAsync(
                persistence,
                [request.Key],
                [selectedApp.Id],
                database =>
                {
                    mutationHandler.UpdateAppPrefixes(database, selectedApp.Id, request.AppPrefixes);
                    mutationHandler.ChangeAppMapping(
                        database,
                        request.Key,
                        request.CurrentAppId,
                        selectedApp,
                        request.ArgumentsTemplate,
                        request.CurrentTemplateInRow,
                        request.CurrentPathPrefixes,
                        request.PathPrefixes,
                        request.CurrentReplacePrefixes,
                        request.ReplacePrefixes,
                        requiresAllowPassingArgumentsEnable: validation.RequiresAllowPassingArgumentsEnable);
                    return null;
                });
            return CreateSubmitResult(result);
        });
    }

    public Task<HandlerMappingDialogSubmitResult> SubmitEditDirectAsync(
        EditDirectHandlerMappingSubmitRequest request,
        IHandlerMappingDialogPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(persistence);

        return ExecuteRetryableAsync(async () =>
        {
            var normalizedNewValue = string.IsNullOrWhiteSpace(request.NewValue)
                ? null
                : request.NewValue.Trim();
            if (normalizedNewValue == null ||
                string.Equals(normalizedNewValue, request.CurrentValue, StringComparison.OrdinalIgnoreCase))
            {
                return Close();
            }

            var validation = dialogHelper.ValidateDirectHandler([request.Key], normalizedNewValue);
            if (!validation.IsValid || validation.DirectHandlerEntries == null)
                return ValidationFailure(validation.ErrorMessage);

            var newEntry = validation.DirectHandlerEntries[0];
            var result = await submitTransaction.SubmitAsync(
                persistence,
                [request.Key],
                [],
                database => mutationHandler.EditDirectHandler(database, request.Key, request.CurrentEntry, newEntry).KeysToRestore);
            return CreateSubmitResult(result);
        });
    }

    public Task<HandlerMappingDialogSubmitResult> SubmitImportAsync(
        ImportAssociationsDialogSubmitRequest request,
        IHandlerMappingDialogPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(persistence);

        return ExecuteRetryableAsync(async () =>
        {
            if (request.SelectedEntries.Count == 0)
                return Close();

            var validation = dialogHelper.ValidateKeys(request.SelectedEntries.Select(entry => entry.Key).ToArray());
            if (validation.Invalid.Count > 0)
            {
                return ValidationFailure(
                    $"Invalid keys: {string.Join(", ", validation.Invalid)}. Use file extensions (.pdf) or protocols (http).");
            }

            var result = await submitTransaction.SubmitAsync(
                persistence,
                request.SelectedEntries.Select(entry => entry.Key).ToArray(),
                [],
                database =>
                {
                    mutationHandler.ApplyImportedAssociations(database, request.SelectedEntries);
                    return null;
                });
            return CreateSubmitResult(result);
        });
    }

    internal static async Task<HandlerMappingDialogSubmitResult> ExecuteRetryableAsync(
        Func<Task<HandlerMappingDialogSubmitResult>>? submitRequested,
        Action<Exception>? reportUnexpectedFailure = null)
    {
        if (submitRequested == null)
            return Close();

        try
        {
            return await submitRequested.Invoke();
        }
        catch (Exception ex)
        {
            try
            {
                reportUnexpectedFailure?.Invoke(ex);
            }
            catch
            {
                // Reporting is best-effort. The dialog must still stay open.
            }

            return UnexpectedFailure(ex.Message);
        }
    }

    private HandlerMappingDialogSubmitResult CreateSubmitResult(HandlerMappingSubmitResult result)
    {
        if (!result.ShouldClose)
        {
            return new HandlerMappingDialogSubmitResult(
                DialogResult: null,
                ValidationMessage: null,
                HasUnresolvedFailure: true,
                UnresolvedFailureText: BuildSaveFailureText(result.SaveError),
                WarningMessage: null,
                UnexpectedErrorMessage: null);
        }

        if (!string.IsNullOrWhiteSpace(result.RegistrySyncWarning))
        {
            log.Warn($"Handler association registry sync failed: {result.RegistrySyncWarning}");
            return new HandlerMappingDialogSubmitResult(
                DialogResult: System.Windows.Forms.DialogResult.OK,
                ValidationMessage: null,
                HasUnresolvedFailure: false,
                UnresolvedFailureText: null,
                WarningMessage: BuildRegistryWarningText(result.RegistrySyncWarning),
                UnexpectedErrorMessage: null);
        }

        return Close();
    }

    private static bool HasEditAppMutation(
        EditAppHandlerMappingSubmitRequest request,
        AppEntry selectedApp,
        HandlerMappingDialogValidationResult validation)
    {
        var existingTemplate = string.IsNullOrEmpty(request.CurrentTemplateInRow)
            ? null
            : request.CurrentTemplateInRow;
        var appPrefixesChanged = !(selectedApp.PathPrefixes ?? [])
            .SequenceEqual(request.AppPrefixes ?? [], StringComparer.OrdinalIgnoreCase);
        var mappingChanged =
            !string.Equals(selectedApp.Id, request.CurrentAppId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.ArgumentsTemplate, existingTemplate, StringComparison.Ordinal) ||
            !(request.CurrentPathPrefixes ?? []).SequenceEqual(request.PathPrefixes ?? [], StringComparer.OrdinalIgnoreCase) ||
            request.CurrentReplacePrefixes != request.ReplacePrefixes ||
            (validation.RequiresAllowPassingArgumentsEnable && !selectedApp.AllowPassingArguments);

        return appPrefixesChanged || mappingChanged;
    }

    private static HandlerMappingDialogSubmitResult Close()
        => new(
            DialogResult: System.Windows.Forms.DialogResult.OK,
            ValidationMessage: null,
            HasUnresolvedFailure: false,
            UnresolvedFailureText: null,
            WarningMessage: null,
            UnexpectedErrorMessage: null);

    private static HandlerMappingDialogSubmitResult ValidationFailure(string? message)
        => new(
            DialogResult: null,
            ValidationMessage: message,
            HasUnresolvedFailure: false,
            UnresolvedFailureText: null,
            WarningMessage: null,
            UnexpectedErrorMessage: null);

    private static HandlerMappingDialogSubmitResult UnexpectedFailure(string? message)
        => new(
            DialogResult: null,
            ValidationMessage: null,
            HasUnresolvedFailure: true,
            UnresolvedFailureText: null,
            WarningMessage: null,
            UnexpectedErrorMessage: BuildSaveFailureText(message));

    private static string BuildSaveFailureText(string? message)
        => $"{SaveFailurePrefix}\n\n{message}";

    private static string BuildRegistryWarningText(string? message)
        => $"{RegistrySyncFailurePrefix}\n\n{message}";
}

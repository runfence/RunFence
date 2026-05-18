using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Pure data helper for <see cref="HandlerMappingsDialog"/>: validates handler keys,
/// resolves direct handler entries, looks up current direct handlers, and detects new capabilities.
/// Contains no dialog state — all dialog-state is passed as parameters.
/// </summary>
public class HandlerMappingDialogHelper(
    IExeAssociationRegistryReader reader,
    IHandlerMappingService handlerMappingService)
{
    /// <summary>
    /// Validates keys against <see cref="AppHandlerRegistrationService.IsValidKey"/>.
    /// Returns a tuple of valid and invalid key lists without showing any UI.
    /// </summary>
    public (List<string> Valid, List<string> Invalid) ValidateKeys(IReadOnlyList<string> keys)
    {
        var valid = new List<string>(keys.Count);
        var invalid = new List<string>();
        foreach (var key in keys)
        {
            if (AppHandlerRegistrationService.IsValidKey(key))
                valid.Add(key);
            else
                invalid.Add(key);
        }
        return (valid, invalid);
    }

    public HandlerMappingDialogValidationResult ValidateAppMapping(
        IReadOnlyList<string> keys,
        AppEntry? selectedApp,
        string? argumentsTemplate,
        IReadOnlyList<string>? appPrefixes,
        IReadOnlyList<string>? pathPrefixes,
        bool replacePrefixes)
    {
        _ = appPrefixes;
        _ = pathPrefixes;
        _ = replacePrefixes;

        var (validKeys, invalidKeys) = ValidateKeys(keys);
        if (invalidKeys.Count > 0)
            return new(validKeys, invalidKeys, BuildInvalidKeyMessage(invalidKeys));

        if (validKeys.Count == 0)
            return new(validKeys, invalidKeys, "At least one extension or protocol is required.");

        if (selectedApp == null)
            return new(validKeys, invalidKeys, "Select an application.");

        var requiresAllowPassingArgumentsEnable = false;
        if (!selectedApp.AllowPassingArguments
            && RequiresAssociationTargetForwarding(selectedApp, argumentsTemplate))
        {
            requiresAllowPassingArgumentsEnable = true;
            return new(
                validKeys,
                invalidKeys,
                null,
                RequiresAllowPassingArgumentsEnable: requiresAllowPassingArgumentsEnable);
        }

        return new(
            validKeys,
            invalidKeys,
            null,
            null,
            RequiresAllowPassingArgumentsEnable: requiresAllowPassingArgumentsEnable);
    }

    public HandlerMappingDialogValidationResult ValidateDirectHandler(
        IReadOnlyList<string> keys,
        string? handlerValue)
    {
        var (validKeys, invalidKeys) = ValidateKeys(keys);
        if (invalidKeys.Count > 0)
            return new(validKeys, invalidKeys, BuildInvalidKeyMessage(invalidKeys));

        if (validKeys.Count == 0)
            return new(validKeys, invalidKeys, "At least one extension or protocol is required.");

        var normalizedHandler = string.IsNullOrWhiteSpace(handlerValue) ? null : handlerValue.Trim();
        if (string.IsNullOrEmpty(normalizedHandler))
            return new(validKeys, invalidKeys, "A direct handler value is required.");

        var resolvedEntries = new List<DirectHandlerEntry>(validKeys.Count);
        foreach (var key in validKeys)
        {
            var entry = ResolveDirectHandlerEntry(key, normalizedHandler);
            if (entry.Command != null
                && !AssociationCommandHelper.TryMaterializeCommand(
                    entry.Command,
                    "__runfence_validation__",
                    out _,
                    out var rejectionReason))
            {
                return new(validKeys, invalidKeys, $"Direct handler for '{key}' is invalid: {rejectionReason}");
            }

            resolvedEntries.Add(entry);
        }

        return new(validKeys, invalidKeys, null, resolvedEntries);
    }

    public HandlerMappingAddCaptureResult CaptureAcceptedValues(HandlerMappingAddCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsEditMode)
        {
            if (request.IsDirectEditMode)
            {
                var validation = ValidateDirectHandler([request.EditKey], request.DirectHandlerValue);
                if (!validation.IsValid)
                    return new(validation.ErrorMessage);

                var newValue = validation.DirectHandlerEntries != null ? request.DirectHandlerValue?.Trim() : null;
                if (!string.IsNullOrEmpty(newValue)
                    && !string.Equals(newValue, request.OriginalDirectValue, StringComparison.OrdinalIgnoreCase))
                {
                    return new(
                        ValidationError: null,
                        DirectHandlerValue: newValue);
                }

                return new(null);
            }

            var editValidation = ValidateAppMapping(
                [request.EditKey],
                request.SelectedApp,
                request.ArgumentsTemplate,
                request.AppPrefixes,
                request.AssociationPrefixes,
                request.ReplacePrefixes);
            if (!editValidation.IsValid)
                return new(editValidation.ErrorMessage);

            return new(
                ValidationError: null,
                SelectedApp: request.SelectedApp,
                ArgumentsTemplate: request.ArgumentsTemplate,
                AppPrefixes: request.AppPrefixes,
                PathPrefixes: request.AssociationPrefixes,
                ReplacePrefixes: request.ReplacePrefixes);
        }

        var rawKey = HandlerAssociationValueNormalization.NormalizeKey(request.RawKey);
        if (string.IsNullOrEmpty(rawKey))
            return new("An extension or protocol is required.");

        var resolvedKeys = string.Equals(
                rawKey,
                HandlerAssociationValueNormalization.CommonOptions[0],
                StringComparison.OrdinalIgnoreCase)
            ? EvaluationConstants.BrowserAssociations.ToArray()
            : [rawKey];

        if (request.IsDirectModeSelected)
        {
            var directValidation = ValidateDirectHandler(resolvedKeys, request.DirectHandlerValue);
            if (!directValidation.IsValid)
                return new(directValidation.ErrorMessage);

            return new(
                ValidationError: null,
                IsDirectMode: true,
                ResolvedKeys: directValidation.ValidKeys,
                DirectHandlerValue: request.DirectHandlerValue);
        }

        var appValidation = ValidateAppMapping(
            resolvedKeys,
            request.SelectedApp,
            request.ArgumentsTemplate,
            request.AppPrefixes,
            request.AssociationPrefixes,
            request.ReplacePrefixes);
        if (!appValidation.IsValid)
            return new(appValidation.ErrorMessage);

        return new(
            ValidationError: null,
            ResolvedKeys: appValidation.ValidKeys,
            SelectedApp: request.SelectedApp,
            ArgumentsTemplate: request.ArgumentsTemplate,
            AppPrefixes: request.AppPrefixes,
            PathPrefixes: request.AssociationPrefixes,
            ReplacePrefixes: request.ReplacePrefixes);
    }

    /// <summary>
    /// Resolves a user-supplied handler value string into a typed <see cref="DirectHandlerEntry"/>.
    /// Extensions with a registered ProgId produce a class-based entry; all others produce a command entry.
    /// </summary>
    public DirectHandlerEntry ResolveDirectHandlerEntry(string key, string handlerValue)
    {
        if (key.StartsWith('.') && reader.IsRegisteredProgId(key, handlerValue))
            return new DirectHandlerEntry { ClassName = handlerValue };

        return new DirectHandlerEntry { Command = handlerValue };
    }

    /// <summary>
    /// Returns the current effective direct handler entry for the given key, or null if none exists.
    /// </summary>
    public DirectHandlerEntry? GetCurrentDirectHandler(string key, IHandlerMappingDialogPersistence persistence)
    {
        var mappings = handlerMappingService.GetEffectiveDirectHandlerMappings(persistence.GetDatabase());
        return mappings.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Returns true when any handler keys have been newly added since the dialog was initialized
    /// (used to prompt the user to open Default Apps).
    /// </summary>
    public bool HasNewCapability(IHandlerMappingDialogPersistence persistence, IReadOnlySet<string> originalRunFenceKeys)
    {
        var currentKeys = handlerMappingService.GetAllHandlerMappings(persistence.GetDatabase()).Keys;
        return currentKeys.Any(k => !originalRunFenceKeys.Contains(k));
    }

    private static bool RequiresAssociationTargetForwarding(AppEntry app, string? argumentsTemplate)
    {
        var probeApp = app.Clone();
        probeApp.AllowPassingArguments = true;

        var first = ProcessLaunchHelper.DetermineArguments(
            probeApp,
            "__runfence_association_target_a__",
            argumentsTemplate);
        var second = ProcessLaunchHelper.DetermineArguments(
            probeApp,
            "__runfence_association_target_b__",
            argumentsTemplate);

        return !string.Equals(first, second, StringComparison.Ordinal);
    }

    private static string BuildInvalidKeyMessage(IReadOnlyList<string> invalidKeys) =>
        $"Invalid keys: {string.Join(", ", invalidKeys)}. Use file extensions (.pdf) or protocols (http).";
}

public sealed record HandlerMappingDialogValidationResult(
    IReadOnlyList<string> ValidKeys,
    IReadOnlyList<string> InvalidKeys,
    string? ErrorMessage,
    IReadOnlyList<DirectHandlerEntry>? DirectHandlerEntries = null,
    bool RequiresAllowPassingArgumentsEnable = false)
{
    public bool IsValid => string.IsNullOrEmpty(ErrorMessage);
}

public sealed record HandlerMappingAddCaptureRequest(
    bool IsEditMode,
    bool IsDirectEditMode,
    string EditKey,
    string OriginalDirectValue,
    string? RawKey,
    bool IsDirectModeSelected,
    AppEntry? SelectedApp,
    string? DirectHandlerValue,
    string? ArgumentsTemplate,
    IReadOnlyList<string>? AppPrefixes,
    IReadOnlyList<string>? AssociationPrefixes,
    bool ReplacePrefixes);

public sealed record HandlerMappingAddCaptureResult(
    string? ValidationError,
    bool IsDirectMode = false,
    IReadOnlyList<string>? ResolvedKeys = null,
    AppEntry? SelectedApp = null,
    string? DirectHandlerValue = null,
    string? ArgumentsTemplate = null,
    IReadOnlyList<string>? AppPrefixes = null,
    IReadOnlyList<string>? PathPrefixes = null,
    bool ReplacePrefixes = false);

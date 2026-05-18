using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public class HandlerMappingAddDialogSubmissionState
{
    private IHandlerMappingDialogPersistence? _persistence;

    public bool IsEditMode { get; private set; }
    public bool IsDirectEditMode { get; private set; }
    public string EditKey { get; private set; } = string.Empty;
    public string CurrentAppId { get; private set; } = string.Empty;
    public string? CurrentTemplateInRow { get; private set; }
    public IReadOnlyList<string>? CurrentPathPrefixes { get; private set; }
    public bool CurrentReplacePrefixes { get; private set; }
    public DirectHandlerEntry? CurrentDirectEntry { get; private set; }
    public string OriginalDirectValue { get; private set; } = string.Empty;

    public bool IsDirectMode { get; private set; }
    public IReadOnlyList<string> ResolvedKeys { get; private set; } = [];
    public AppEntry? SelectedApp { get; private set; }
    public string? DirectHandlerValue { get; private set; }
    public string? ArgumentsTemplate { get; private set; }
    public IReadOnlyList<string>? AppPrefixes { get; private set; }
    public IReadOnlyList<string>? PathPrefixes { get; private set; }
    public bool ReplacePrefixes { get; private set; }

    public IHandlerMappingDialogPersistence Persistence =>
        _persistence ?? throw new InvalidOperationException("Handler mapping dialog must be initialized before submission.");

    public void InitializeAdd(IHandlerMappingDialogPersistence persistence)
    {
        InitializeCore(persistence);
        IsEditMode = false;
        IsDirectEditMode = false;
    }

    public void InitializeEditApp(
        string key,
        string currentAppId,
        string? currentTemplate,
        IHandlerMappingDialogPersistence persistence,
        IReadOnlyList<string>? currentAssocPrefixes,
        bool currentReplacePrefixes)
    {
        InitializeCore(persistence);
        IsEditMode = true;
        IsDirectEditMode = false;
        EditKey = key;
        CurrentAppId = currentAppId;
        CurrentTemplateInRow = currentTemplate;
        CurrentPathPrefixes = currentAssocPrefixes;
        CurrentReplacePrefixes = currentReplacePrefixes;
    }

    public void InitializeEditDirect(
        string key,
        string currentValue,
        DirectHandlerEntry currentEntry,
        IHandlerMappingDialogPersistence persistence)
    {
        InitializeCore(persistence);
        IsEditMode = true;
        IsDirectEditMode = true;
        EditKey = key;
        CurrentDirectEntry = currentEntry;
        OriginalDirectValue = currentValue;
    }

    public HandlerMappingAddCaptureRequest CreateCaptureRequest(
        string? rawKey,
        bool isDirectModeSelected,
        AppEntry? selectedApp,
        string? directHandlerValue,
        string? argumentsTemplate,
        IReadOnlyList<string>? appPrefixes,
        IReadOnlyList<string>? associationPrefixes,
        bool replacePrefixes)
    {
        return new HandlerMappingAddCaptureRequest(
            IsEditMode,
            IsDirectEditMode,
            EditKey,
            OriginalDirectValue,
            rawKey,
            isDirectModeSelected,
            selectedApp,
            directHandlerValue,
            argumentsTemplate,
            appPrefixes,
            associationPrefixes,
            replacePrefixes);
    }

    public void ApplyCapture(HandlerMappingAddCaptureResult capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        IsDirectMode = capture.IsDirectMode;
        ResolvedKeys = capture.ResolvedKeys ?? [];
        SelectedApp = capture.SelectedApp;
        DirectHandlerValue = capture.DirectHandlerValue;
        ArgumentsTemplate = capture.ArgumentsTemplate;
        AppPrefixes = capture.AppPrefixes;
        PathPrefixes = capture.PathPrefixes;
        ReplacePrefixes = capture.ReplacePrefixes;
    }

    public HandlerMappingAddDialogSubmitRequest CreateAddRequest()
        => new(
            IsDirectMode,
            ResolvedKeys,
            SelectedApp,
            DirectHandlerValue,
            ArgumentsTemplate,
            AppPrefixes,
            PathPrefixes,
            ReplacePrefixes);

    public EditDirectHandlerMappingSubmitRequest CreateEditDirectRequest()
    {
        var currentEntry = CurrentDirectEntry
            ?? throw new InvalidOperationException("Direct edit submission context was not initialized.");
        return new EditDirectHandlerMappingSubmitRequest(
            EditKey,
            currentEntry,
            OriginalDirectValue,
            DirectHandlerValue);
    }

    public EditAppHandlerMappingSubmitRequest CreateEditAppRequest()
        => new(
            EditKey,
            SelectedApp,
            ArgumentsTemplate,
            AppPrefixes,
            PathPrefixes,
            ReplacePrefixes,
            CurrentAppId,
            CurrentTemplateInRow,
            CurrentPathPrefixes,
            CurrentReplacePrefixes);

    private void InitializeCore(IHandlerMappingDialogPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        EditKey = string.Empty;
        CurrentAppId = string.Empty;
        CurrentTemplateInRow = null;
        CurrentPathPrefixes = null;
        CurrentReplacePrefixes = false;
        CurrentDirectEntry = null;
        OriginalDirectValue = string.Empty;
        IsDirectMode = false;
        ResolvedKeys = [];
        SelectedApp = null;
        DirectHandlerValue = null;
        ArgumentsTemplate = null;
        AppPrefixes = null;
        PathPrefixes = null;
        ReplacePrefixes = false;
    }
}

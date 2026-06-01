using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dialog for adding a new handler association (app-based or direct handler),
/// or editing an existing one.
/// Call one of <see cref="Initialize"/>, <see cref="InitializeForEditApp"/>,
/// or <see cref="InitializeForEditDirect"/> before <see cref="Form.ShowDialog()"/>.
/// After acceptance, read result via <see cref="IsDirectMode"/>, <see cref="ResolvedKeys"/>,
/// <see cref="SelectedApp"/>, <see cref="DirectHandlerValue"/>, <see cref="ArgumentsTemplate"/>,
/// <see cref="AppPrefixes"/>, <see cref="PathPrefixes"/>, and <see cref="ReplacePrefixes"/>.
/// </summary>
public interface IHandlerMappingAddDialog : IWin32Window, IDisposable
{
    bool HasUnresolvedSubmitFailure { get; }
    bool IsDirectMode { get; }
    IReadOnlyList<string> ResolvedKeys { get; }
    AppEntry? SelectedApp { get; }
    string? DirectHandlerValue { get; }
    string? ArgumentsTemplate { get; }
    IReadOnlyList<string>? AppPrefixes { get; }
    IReadOnlyList<string>? PathPrefixes { get; }
    bool ReplacePrefixes { get; }
    void Initialize(IReadOnlyList<AppEntry> apps, IHandlerMappingDialogPersistence persistence);
    void InitializeForEditApp(string key, IReadOnlyList<AppEntry> apps, AppEntry? currentApp,
        string currentAppId, string? currentTemplate, IHandlerMappingDialogPersistence persistence,
        IReadOnlyList<string>? currentAppPrefixes = null,
        IReadOnlyList<string>? currentAssocPrefixes = null, bool currentReplacePrefixes = false);
    void InitializeForEditDirect(
        string key,
        string currentValue,
        DirectHandlerEntry currentEntry,
        IHandlerMappingDialogPersistence persistence);
    DialogResult ShowDialog(IWin32Window owner);
}

public partial class HandlerMappingAddDialog : RunFence.UI.Forms.ContextHelpForm, IHandlerMappingAddDialog
{
    public const int AppMappingHeight = 580;
    public const int AppEditHeight = 500;
    public const int DirectEditHeight = 160;

    private readonly HandlerMappingAppModePresenter _appPresenter;
    private readonly HandlerMappingDirectModePresenter _directPresenter;
    private readonly HandlerMappingLayoutController _layoutController;
    private readonly HandlerMappingDialogHelper _dialogHelper;
    private readonly HandlerMappingDialogSubmissionCoordinator _submissionCoordinator;
    private readonly HandlerMappingAddDialogSubmissionState _submissionState;
    private readonly IMessageBoxService _messageBoxService;

    /// <summary>True when the user selected the Direct Handler mode (add mode only).</summary>
    public bool IsDirectMode => _submissionState.IsDirectMode;

    /// <summary>
    /// The resolved association keys (add mode only). "Browser" is expanded to http/https/.htm/.html.
    /// Empty until OK is accepted with a valid key.
    /// </summary>
    public IReadOnlyList<string> ResolvedKeys => _submissionState.ResolvedKeys;

    /// <summary>The selected application (app mode). Null until OK is accepted.</summary>
    public AppEntry? SelectedApp => _submissionState.SelectedApp;

    /// <summary>
    /// The trimmed handler value (direct handler mode). Null until OK is accepted.
    /// In edit direct mode, null means the value was unchanged.
    /// </summary>
    public string? DirectHandlerValue => _submissionState.DirectHandlerValue;

    /// <summary>The arguments template (app mode), or null if blank. Null until OK is accepted.</summary>
    public string? ArgumentsTemplate => _submissionState.ArgumentsTemplate;

    /// <summary>The updated app-level path prefixes (app mode). Null until OK is accepted.</summary>
    public IReadOnlyList<string>? AppPrefixes => _submissionState.AppPrefixes;

    /// <summary>The per-association path prefix overrides (app mode). Null until OK is accepted.</summary>
    public IReadOnlyList<string>? PathPrefixes => _submissionState.PathPrefixes;

    /// <summary>True when the association prefixes replace (rather than add to) the app-level prefixes.</summary>
    public bool ReplacePrefixes => _submissionState.ReplacePrefixes;

    public bool HasUnresolvedSubmitFailure { get; private set; }

    public HandlerMappingAddDialog(
        IExeAssociationRegistryReader reader,
        IInteractiveUserAssociationReader interactiveReader,
        HandlerMappingDialogHelper dialogHelper,
        HandlerMappingDialogSubmissionCoordinator submissionCoordinator,
        HandlerMappingAddDialogSubmissionState submissionState,
        IMessageBoxService messageBoxService)
    {
        InitializeComponent();
        _dialogHelper = dialogHelper;
        _submissionCoordinator = submissionCoordinator;
        _submissionState = submissionState;
        _messageBoxService = messageBoxService;
        _appPresenter = new HandlerMappingAppModePresenter(
            reader, _appCombo, _keyCombo, _templateTextBox, _combinedPrefixesSection);
        _directPresenter = new HandlerMappingDirectModePresenter(
            interactiveReader, _keyCombo, _handlerTextBox);
        _layoutController = new HandlerMappingLayoutController(
            this, _layout, _modeToolStrip,
            _keyLabel, _keyCombo,
            _appLabel, _appCombo,
            _handlerLabel, _handlerTextBox,
            _templateLabel, _templateTextBox,
            _combinedPrefixesSection);
        _okButton.DialogResult = DialogResult.None;
        _okButton.Click += OnOkClick;
        RegisterContextHelp();
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_templateTextBox, ContextHelpTextCatalog.Launch_Arguments);
        _combinedPrefixesSection.RegisterContextHelp(this);
    }

    /// <summary>
    /// Populates the app combo with available apps for add mode.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(IReadOnlyList<AppEntry> apps, IHandlerMappingDialogPersistence persistence)
    {
        _submissionState.InitializeAdd(persistence);
        HasUnresolvedSubmitFailure = false;
        _appPresenter.PopulateApps(apps);
        _combinedPrefixesSection.SetAssociationPrefixes(null, false);
        _appPresenter.RebuildKeyComboSuggestions();
        UpdateAddModeOkState();
    }

    /// <summary>
    /// Populates the dialog for editing an existing app-based handler mapping.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void InitializeForEditApp(string key, IReadOnlyList<AppEntry> apps, AppEntry? currentApp,
        string currentAppId, string? currentTemplate, IHandlerMappingDialogPersistence persistence,
        IReadOnlyList<string>? currentAppPrefixes = null,
        IReadOnlyList<string>? currentAssocPrefixes = null, bool currentReplacePrefixes = false)
    {
        _submissionState.InitializeEditApp(
            key,
            currentAppId,
            currentTemplate,
            persistence,
            currentAssocPrefixes,
            currentReplacePrefixes);
        HasUnresolvedSubmitFailure = false;
        Text = $"Edit Association \u2014 {key}";
        _appLabel.Text = $"Application for \"{key}\":";

        _appPresenter.PopulateAppsForEdit(apps, currentApp);
        _templateTextBox.Text = currentTemplate ?? string.Empty;
        _appPresenter.LoadPrefixes(new CombinedPrefixesState(
            currentAppPrefixes,
            currentAssocPrefixes,
            currentReplacePrefixes));

        _layoutController.ApplyEditLayout(directMode: false);
    }

    /// <summary>
    /// Populates the dialog for editing an existing direct handler mapping.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void InitializeForEditDirect(
        string key,
        string currentValue,
        DirectHandlerEntry currentEntry,
        IHandlerMappingDialogPersistence persistence)
    {
        _submissionState.InitializeEditDirect(key, currentValue, currentEntry, persistence);
        HasUnresolvedSubmitFailure = false;
        Text = $"Edit Direct Handler \u2014 {key}";
        _handlerLabel.Text = $"Handler for \"{key}\" (class name or command):";
        _handlerTextBox.Text = currentValue;

        _layoutController.ApplyEditLayout(directMode: true);
        UpdateEditDirectOkState();
    }

    private void OnRadioAppCheckedChanged(object? sender, EventArgs e)
    {
        if (_radioApp.Checked)
        {
            _layoutController.SwitchAddMode(directMode: false);
            _appPresenter.RebuildKeyComboSuggestions();
            UpdateAddModeOkState();
        }
    }

    private void OnRadioDirectCheckedChanged(object? sender, EventArgs e)
    {
        if (_radioDirect.Checked)
        {
            _layoutController.SwitchAddMode(directMode: true);
            _directPresenter.RebuildKeyComboSuggestions();
            _directPresenter.TryAutoFillHandler();
            UpdateAddModeOkState();
        }
    }

    private void OnAppComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_radioApp.Checked)
        {
            if (!_submissionState.IsEditMode)
                _appPresenter.RebuildKeyComboSuggestions();
            UpdateTemplate();
        }

        _appPresenter.OnAppSelectionChanged();
        UpdateAddModeOkState();
    }

    private void OnKeyComboTextChanged(object? sender, EventArgs e)
    {
        if (_radioDirect.Checked)
            _directPresenter.TryAutoFillHandler();
        else
            UpdateTemplate();
        UpdateAddModeOkState();
    }

    private void OnHandlerTextChanged(object? sender, EventArgs e)
    {
        _directPresenter.NotifyHandlerTextEditedByUser();

        if (_submissionState.IsEditMode && _submissionState.IsDirectEditMode)
            UpdateEditDirectOkState();
        else if (!_submissionState.IsEditMode)
            UpdateAddModeOkState();
    }

    private void UpdateEditDirectOkState()
    {
        _okButton.Enabled = !string.IsNullOrWhiteSpace(_handlerTextBox.Text);
    }

    private void UpdateAddModeOkState()
    {
        if (_submissionState.IsEditMode)
            return;

        var hasKey = !string.IsNullOrWhiteSpace(_keyCombo.Text);
        var hasTarget = _radioApp.Checked
            ? _appPresenter.SelectedApp != null
            : !string.IsNullOrWhiteSpace(_handlerTextBox.Text);
        _okButton.Enabled = hasKey && hasTarget;
    }

    private void UpdateTemplate()
    {
        var keyText = _submissionState.IsEditMode ? _submissionState.EditKey : _keyCombo.Text;
        _appPresenter.UpdateTemplate(keyText);
    }

    private string? TryCaptureAcceptedValues()
    {
        var prefixesState = _appPresenter.CapturePrefixesState();
        var capture = _dialogHelper.CaptureAcceptedValues(_submissionState.CreateCaptureRequest(
            rawKey: _keyCombo.Text,
            isDirectModeSelected: _radioDirect.Checked,
            selectedApp: _appPresenter.SelectedApp,
            directHandlerValue: _submissionState.IsEditMode ? _handlerTextBox.Text : _directPresenter.HandlerValue,
            argumentsTemplate: _appPresenter.NormalizedTemplate,
            appPrefixes: prefixesState.AppPrefixes,
            associationPrefixes: prefixesState.AssociationPrefixes,
            replacePrefixes: prefixesState.ReplacePrefixes));
        _submissionState.ApplyCapture(capture);
        return capture.ValidationError;
    }

    private void ShowValidationError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _messageBoxService.Show(this, message, "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _keyCombo.Focus();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        HandleOk();
    }

    private void HandleOk()
    {
        if (IsDisposed || Disposing)
            return;

        var validationError = TryCaptureAcceptedValues();
        if (validationError != null)
        {
            ShowValidationError(validationError);
            return;
        }

        HasUnresolvedSubmitFailure = false;

        try
        {
            Enabled = false;
            var result = Submit();
            if (IsDisposed || Disposing)
                return;

            ApplySubmitResult(result);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
                Enabled = true;
        }
    }

    private HandlerMappingDialogSubmitResult Submit()
    {
        var persistence = _submissionState.Persistence;
        if (!_submissionState.IsEditMode)
            return _submissionCoordinator.SubmitAdd(_submissionState.CreateAddRequest(), persistence);

        return _submissionState.IsDirectEditMode
            ? _submissionCoordinator.SubmitEditDirect(_submissionState.CreateEditDirectRequest(), persistence)
            : _submissionCoordinator.SubmitEditApp(_submissionState.CreateEditAppRequest(), persistence);
    }

    private void ApplySubmitResult(HandlerMappingDialogSubmitResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ValidationMessage))
        {
            ShowValidationError(result.ValidationMessage);
            return;
        }

        HasUnresolvedSubmitFailure = result.HasUnresolvedFailure;

        if (!string.IsNullOrWhiteSpace(result.UnresolvedFailureText))
        {
            ShowSaveFailure(result.UnresolvedFailureText);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.UnexpectedErrorMessage))
        {
            ShowSaveFailure(result.UnexpectedErrorMessage);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
        {
            _messageBoxService.Show(
                this,
                result.WarningMessage,
                "Handler Associations",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (result.DialogResult == System.Windows.Forms.DialogResult.OK)
        {
            DialogResult = System.Windows.Forms.DialogResult.OK;
            Close();
        }
    }

    private void ShowSaveFailure(string message)
    {
        _messageBoxService.Show(
            this,
            message,
            "Save Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

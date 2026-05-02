using RunFence.Core;
using RunFence.Core.Models;

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
public partial class HandlerMappingAddDialog : Form
{
    public const int AppMappingHeight = 580;
    public const int AppEditHeight = 500;
    public const int DirectEditHeight = 160;

    private readonly HandlerMappingAppModePresenter _appPresenter;
    private readonly HandlerMappingDirectModePresenter _directPresenter;
    private readonly HandlerMappingLayoutController _layoutController;

    private bool _isEditMode;
    private bool _isDirectEditMode;
    private string _originalDirectValue = string.Empty;
    private string _editKey = string.Empty;

    /// <summary>True when the user selected the Direct Handler mode (add mode only).</summary>
    public bool IsDirectMode { get; private set; }

    /// <summary>
    /// The resolved association keys (add mode only). "Browser" is expanded to http/https/.htm/.html.
    /// Empty until OK is accepted with a valid key.
    /// </summary>
    public IReadOnlyList<string> ResolvedKeys { get; private set; } = [];

    /// <summary>The selected application (app mode). Null until OK is accepted.</summary>
    public AppEntry? SelectedApp { get; private set; }

    /// <summary>
    /// The trimmed handler value (direct handler mode). Null until OK is accepted.
    /// In edit direct mode, null means the value was unchanged.
    /// </summary>
    public string? DirectHandlerValue { get; private set; }

    /// <summary>The arguments template (app mode), or null if blank. Null until OK is accepted.</summary>
    public string? ArgumentsTemplate { get; private set; }

    /// <summary>The updated app-level path prefixes (app mode). Null until OK is accepted.</summary>
    public IReadOnlyList<string>? AppPrefixes { get; private set; }

    /// <summary>The per-association path prefix overrides (app mode). Null until OK is accepted.</summary>
    public IReadOnlyList<string>? PathPrefixes { get; private set; }

    /// <summary>True when the association prefixes replace (rather than add to) the app-level prefixes.</summary>
    public bool ReplacePrefixes { get; private set; }

    public HandlerMappingAddDialog(
        IExeAssociationRegistryReader reader,
        IInteractiveUserAssociationReader interactiveReader)
    {
        InitializeComponent();
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
    }

    /// <summary>
    /// Populates the app combo with available apps for add mode.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(IReadOnlyList<AppEntry> apps)
    {
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
        string? currentTemplate, IReadOnlyList<string>? currentAppPrefixes = null,
        IReadOnlyList<string>? currentAssocPrefixes = null, bool currentReplacePrefixes = false)
    {
        _isEditMode = true;
        _editKey = key;
        Text = $"Edit Association — {key}";
        _appLabel.Text = $"Application for \"{key}\":";

        _appPresenter.PopulateAppsForEdit(apps, currentApp);
        _templateTextBox.Text = currentTemplate ?? string.Empty;
        _appPresenter.LoadPrefixes(currentAppPrefixes, currentAssocPrefixes, currentReplacePrefixes);

        _layoutController.ApplyEditLayout(directMode: false);
    }

    /// <summary>
    /// Populates the dialog for editing an existing direct handler mapping.
    /// Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void InitializeForEditDirect(string key, string currentValue)
    {
        _isEditMode = true;
        _isDirectEditMode = true;
        _originalDirectValue = currentValue;
        Text = $"Edit Direct Handler — {key}";
        _handlerLabel.Text = $"Handler for \"{key}\" (class name or command):";
        _handlerTextBox.Text = currentValue;

        _layoutController.ApplyEditLayout(directMode: true);
        UpdateEditDirectOkState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult != DialogResult.OK)
            return;

        if (_isEditMode)
        {
            if (_isDirectEditMode)
            {
                var newValue = _handlerTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(newValue) &&
                    !string.Equals(newValue, _originalDirectValue, StringComparison.OrdinalIgnoreCase))
                    DirectHandlerValue = newValue;
            }
            else
            {
                if (_appPresenter.SelectedApp is not { } selected)
                {
                    e.Cancel = true;
                    return;
                }
                SelectedApp = selected;
                ArgumentsTemplate = _appPresenter.NormalizedTemplate;
                AppPrefixes = _appPresenter.AppPrefixes;
                PathPrefixes = _appPresenter.AssociationPrefixes;
                ReplacePrefixes = _appPresenter.IsReplace;
            }
        }
        else
        {
            var rawKey = HandlerAssociationDialogValueHelper.NormalizeKey(_keyCombo.Text);
            if (string.IsNullOrEmpty(rawKey))
            {
                e.Cancel = true;
                return;
            }

            ResolvedKeys = string.Equals(
                    rawKey, HandlerAssociationDialogValueHelper.CommonOptions[0],
                    StringComparison.OrdinalIgnoreCase)
                ? EvaluationConstants.BrowserAssociations.ToArray()
                : [rawKey];
            IsDirectMode = _radioDirect.Checked;

            if (IsDirectMode)
            {
                DirectHandlerValue = _directPresenter.HandlerValue;
            }
            else
            {
                if (_appPresenter.SelectedApp is not { } selectedApp)
                {
                    e.Cancel = true;
                    return;
                }
                SelectedApp = selectedApp;
                ArgumentsTemplate = _appPresenter.NormalizedTemplate;
                AppPrefixes = _appPresenter.AppPrefixes;
                PathPrefixes = _appPresenter.AssociationPrefixes;
                ReplacePrefixes = _appPresenter.IsReplace;
            }
        }
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
            if (!_isEditMode)
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
        if (_isEditMode && _isDirectEditMode)
            UpdateEditDirectOkState();
        else if (!_isEditMode)
            UpdateAddModeOkState();
    }

    private void UpdateEditDirectOkState()
    {
        _okButton.Enabled = !string.IsNullOrWhiteSpace(_handlerTextBox.Text);
    }

    private void UpdateAddModeOkState()
    {
        if (_isEditMode) return;
        var hasKey = !string.IsNullOrWhiteSpace(_keyCombo.Text);
        var hasTarget = _radioApp.Checked
            ? _appPresenter.SelectedApp != null
            : !string.IsNullOrWhiteSpace(_handlerTextBox.Text);
        _okButton.Enabled = hasKey && hasTarget;
    }

    private void UpdateTemplate()
    {
        var keyText = _isEditMode ? _editKey : _keyCombo.Text;
        _appPresenter.UpdateTemplate(keyText);
    }
}

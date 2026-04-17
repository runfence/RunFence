using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dialog for adding a new handler association (app-based or direct handler).
/// Call <see cref="Initialize"/> before <see cref="Form.ShowDialog()"/>.
/// After acceptance, read result via <see cref="IsDirectMode"/>, <see cref="ResolvedKeys"/>,
/// <see cref="SelectedAppId"/>, <see cref="DirectHandlerValue"/>, and <see cref="ArgumentsTemplate"/>.
/// </summary>
public partial class HandlerMappingAddDialog : Form
{
    private static readonly string[] CommonOptions =
        ["Browser (http, https, .htm, .html)", .. AppHandlerRegistrationService.CommonAssociationSuggestions];

    private readonly IExeAssociationRegistryReader _reader;
    private readonly IInteractiveUserAssociationReader _interactiveReader;

    /// <summary>True when the user selected the Direct Handler mode.</summary>
    public bool IsDirectMode { get; private set; }

    /// <summary>
    /// The resolved association keys. "Browser" is expanded to http/https/.htm/.html.
    /// Empty until OK is accepted with a valid key.
    /// </summary>
    public IReadOnlyList<string> ResolvedKeys { get; private set; } = [];

    /// <summary>The app ID of the selected application (app mode only). Null until OK is accepted.</summary>
    public string? SelectedAppId { get; private set; }

    /// <summary>The trimmed handler value (direct handler mode only). Null until OK is accepted.</summary>
    public string? DirectHandlerValue { get; private set; }

    /// <summary>The arguments template (app mode only), or null if blank. Null until OK is accepted.</summary>
    public string? ArgumentsTemplate { get; private set; }

    public HandlerMappingAddDialog(
        IExeAssociationRegistryReader reader,
        IInteractiveUserAssociationReader interactiveReader)
    {
        _reader = reader;
        _interactiveReader = interactiveReader;
        InitializeComponent();
    }

    /// <summary>
    /// Populates the app combo with available apps. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(IReadOnlyList<AppEntry> apps)
    {
        foreach (var app in apps.Where(a => a is { IsFolder: false, IsUrlScheme: false }).OrderBy(a => a.Name))
            _appCombo.Items.Add(new AppComboItem(app));
        if (_appCombo.Items.Count > 0)
            _appCombo.SelectedIndex = 0;

        RebuildKeyComboSuggestions();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult != DialogResult.OK)
            return;

        var rawKey = _keyCombo.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(rawKey))
        {
            e.Cancel = true;
            return;
        }

        ResolvedKeys = string.Equals(rawKey, CommonOptions[0], StringComparison.OrdinalIgnoreCase)
            ? Constants.BrowserAssociations.ToArray()
            : [rawKey];
        IsDirectMode = _radioDirect.Checked;

        if (IsDirectMode)
        {
            DirectHandlerValue = _handlerTextBox.Text.Trim();
        }
        else
        {
            if (_appCombo.SelectedItem is not AppComboItem selectedApp)
            {
                e.Cancel = true;
                return;
            }
            SelectedAppId = selectedApp.App.Id;
            ArgumentsTemplate = string.IsNullOrWhiteSpace(_templateTextBox.Text) ? null : _templateTextBox.Text.Trim();
        }
    }

    private void OnRadioAppCheckedChanged(object? sender, EventArgs e)
    {
        if (_radioApp.Checked)
            SwitchMode(directMode: false);
    }

    private void OnRadioDirectCheckedChanged(object? sender, EventArgs e)
    {
        if (_radioDirect.Checked)
            SwitchMode(directMode: true);
    }

    private void OnAppComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_radioApp.Checked)
        {
            RebuildKeyComboSuggestions();
            UpdateTemplate();
        }
    }

    private void OnKeyComboTextChanged(object? sender, EventArgs e)
    {
        if (_radioDirect.Checked)
            TryAutoFillHandler();
        else
            UpdateTemplate();
    }

    private void SwitchMode(bool directMode)
    {
        _appLabel.Visible = !directMode;
        _appCombo.Visible = !directMode;
        _handlerLabel.Visible = directMode;
        _handlerTextBox.Visible = directMode;
        _templateLabel.Visible = !directMode;
        _templateTextBox.Visible = !directMode;

        RebuildKeyComboSuggestions();

        if (directMode)
            TryAutoFillHandler();
    }

    private void TryAutoFillHandler()
    {
        var key = _keyCombo.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(key) && AppHandlerRegistrationService.IsValidKey(key))
        {
            var handler = _interactiveReader.GetAssociationHandler(key);
            if (handler.HasValue)
                _handlerTextBox.Text = handler.Value.ClassName ?? handler.Value.Command ?? string.Empty;
        }
    }

    private void UpdateTemplate()
    {
        var keyText = _keyCombo.Text.Trim().ToLowerInvariant();
        var lookupKey = string.Equals(keyText, CommonOptions[0], StringComparison.OrdinalIgnoreCase) ? "http" : keyText;
        var exePath = (_appCombo.SelectedItem as AppComboItem)?.App.ExePath;
        if (string.IsNullOrEmpty(exePath))
            return;
        var args = AppHandlerRegistrationService.IsValidKey(lookupKey)
            ? _reader.GetNonDefaultArguments(exePath, lookupKey)
            : null;
        _templateTextBox.Text = args ?? "\"%1\"";
    }

    private void RebuildKeyComboSuggestions()
    {
        var current = _keyCombo.Text;
        _keyCombo.Items.Clear();
        if (_radioApp.Checked)
        {
            var exePath = (_appCombo.SelectedItem as AppComboItem)?.App.ExePath;
            IEnumerable<string> items;
            if (!string.IsNullOrEmpty(exePath))
            {
                var registryKeys = _reader.GetHandledAssociations(exePath);
                items = registryKeys.Concat(
                    CommonOptions.Except(registryKeys, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                items = CommonOptions;
            }
            _keyCombo.Items.AddRange(items.Cast<object>().ToArray());
        }
        else
        {
            _keyCombo.Items.AddRange(CommonOptions.Cast<object>().ToArray());
        }
        _keyCombo.Text = current;
    }
}

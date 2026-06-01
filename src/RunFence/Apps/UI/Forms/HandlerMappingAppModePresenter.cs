using RunFence.Core.Models;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Manages app-mode state in <see cref="HandlerMappingAddDialog"/>: populates the app combo,
/// updates the template text when the key or app changes, loads and collects prefix data, and
/// commits the accepted edit-app result.
/// </summary>
internal sealed class HandlerMappingAppModePresenter
{
    private readonly IExeAssociationRegistryReader _reader;
    private readonly HandlerAssociationDialogValueHelper _valueHelper;
    private readonly ComboBox _appCombo;
    private readonly ComboBox _keyCombo;
    private readonly TextBox _templateTextBox;
    private readonly CombinedPrefixesSection _combinedPrefixesSection;

    // Tracks the last app ID for which app-section prefixes were refreshed from the app itself.
    // Prevents spurious re-refreshes when WinForms re-fires SelectedIndexChanged during handle creation.
    private string? _lastRefreshedAppId;

    // Stores the most recently confirmed selected app. WinForms may report SelectedItem = null
    // when the combo has no window handle yet, so this cache ensures result collection works
    // even when Close() is called on a non-shown form.
    private AppEntry? _confirmedSelectedApp;

    public HandlerMappingAppModePresenter(
        IExeAssociationRegistryReader reader,
        ComboBox appCombo,
        ComboBox keyCombo,
        TextBox templateTextBox,
        CombinedPrefixesSection combinedPrefixesSection)
    {
        _reader = reader;
        _valueHelper = new HandlerAssociationDialogValueHelper(reader);
        _appCombo = appCombo;
        _keyCombo = keyCombo;
        _templateTextBox = templateTextBox;
        _combinedPrefixesSection = combinedPrefixesSection;
    }

    /// <summary>Populates the app combo for add mode. Selects the first item if available.</summary>
    public void PopulateApps(IReadOnlyList<AppEntry> apps)
    {
        foreach (var app in apps.Where(a => a is { IsFolder: false, IsUrlScheme: false }).OrderBy(a => a.Name))
            _appCombo.Items.Add(new AppComboItem(app));
        if (_appCombo.Items.Count > 0)
            _appCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Populates the app combo for edit mode, selecting the current app (or the first item if not found).
    /// </summary>
    public void PopulateAppsForEdit(IReadOnlyList<AppEntry> apps, AppEntry? currentApp)
    {
        foreach (var app in apps.Where(a => a is { IsFolder: false, IsUrlScheme: false }).OrderBy(a => a.Name))
            _appCombo.Items.Add(new AppComboItem(app));

        for (var i = 0; i < _appCombo.Items.Count; i++)
        {
            if (_appCombo.Items[i] is AppComboItem item &&
                string.Equals(item.App.Id, currentApp?.Id, StringComparison.OrdinalIgnoreCase))
            {
                _confirmedSelectedApp = item.App;
                _appCombo.SelectedIndex = i;
                return;
            }
        }
        if (_appCombo is { SelectedIndex: < 0, Items.Count: > 0 })
        {
            _confirmedSelectedApp = (_appCombo.Items[0] as AppComboItem)?.App;
            _appCombo.SelectedIndex = 0;
        }
    }

    /// <summary>Returns the currently selected app, or null if nothing is selected.</summary>
    public AppEntry? SelectedApp
    {
        get
        {
            if (_appCombo.SelectedItem is AppComboItem item)
                return item.App;
            var idx = _appCombo.SelectedIndex;
            if (idx >= 0 && idx < _appCombo.Items.Count && _appCombo.Items[idx] is AppComboItem fallback)
                return fallback.App;
            return _confirmedSelectedApp;
        }
    }

    /// <summary>Returns the normalized arguments template from the text box, or null if blank.</summary>
    public string? NormalizedTemplate
        => HandlerAssociationDialogValueHelper.NormalizeTemplate(_templateTextBox.Text);

    /// <summary>Returns the current combined prefix state from the section.</summary>
    public CombinedPrefixesState CapturePrefixesState()
        => new(
            _combinedPrefixesSection.GetAppPrefixes(),
            _combinedPrefixesSection.GetAssociationPrefixes(),
            _combinedPrefixesSection.IsReplace);

    /// <summary>Loads app-level and association prefixes with the given replace flag.</summary>
    public void LoadPrefixes(CombinedPrefixesState state)
    {
        _combinedPrefixesSection.SetAppPrefixes(state.AppPrefixes);
        _combinedPrefixesSection.SetAssociationPrefixes(state.AssociationPrefixes, state.ReplacePrefixes);
    }

    /// <summary>
    /// Updates the template text box based on the current key and selected app.
    /// In edit mode, <paramref name="keyText"/> is the fixed edit key; in add mode it is <see cref="ComboBox.Text"/>.
    /// </summary>
    public void UpdateTemplate(string keyText)
    {
        var commonOptions = HandlerAssociationDialogValueHelper.CommonOptions;
        var lookupKey = string.Equals(keyText.Trim(), commonOptions[0], StringComparison.OrdinalIgnoreCase)
            ? "http"
            : HandlerAssociationDialogValueHelper.NormalizeKey(keyText);
        var selectedApp = SelectedApp;
        var exePath = selectedApp?.ExePath;
        _templateTextBox.Text = string.IsNullOrEmpty(exePath)
            ? HandlerAssociationDialogValueHelper.DefaultArgumentsTemplate
            : _valueHelper.ResolveTemplate(exePath, lookupKey, selectedApp?.AccountSid);
    }

    /// <summary>
    /// Rebuilds the key combo suggestions for app mode. Merges registry-handled keys with common options.
    /// Call after app selection or mode changes (add mode only).
    /// </summary>
    public void RebuildKeyComboSuggestions()
    {
        var current = _keyCombo.Text;
        _keyCombo.Items.Clear();

        var commonOptions = HandlerAssociationDialogValueHelper.CommonOptions;
        var selectedApp = SelectedApp;
        var exePath = selectedApp?.ExePath;
        IEnumerable<string> items;
        if (!string.IsNullOrEmpty(exePath))
        {
            var registryKeys = _reader.GetHandledAssociations(exePath, selectedApp?.AccountSid);
            items = registryKeys.Concat(
                commonOptions.Except(registryKeys, StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            items = commonOptions;
        }
        _keyCombo.Items.AddRange(items.Cast<object>().ToArray());
        _keyCombo.Text = current;
    }

    /// <summary>
    /// Reacts to a change in the selected app: refreshes app-level prefixes loaded into the section.
    /// Skips the refresh when the selected app has not changed (e.g., WinForms re-fires
    /// <see cref="ComboBox.SelectedIndexChanged"/> during handle creation with the same item still selected).
    /// </summary>
    public void OnAppSelectionChanged()
    {
        var selectedApp = SelectedApp;
        var newId = selectedApp?.Id;
        if (string.Equals(newId, _lastRefreshedAppId, StringComparison.OrdinalIgnoreCase))
            return;
        _lastRefreshedAppId = newId;
        if (selectedApp != null)
            _confirmedSelectedApp = selectedApp;
        _combinedPrefixesSection.SetAppPrefixes(selectedApp?.PathPrefixes?.AsReadOnly());
    }
}

using Microsoft.Win32;
using RunFence.Apps.UI;

namespace RunFence.Account.UI.AppContainer;

/// <summary>
/// Dialog for browsing registered COM AppID objects from the registry.
/// Enumerates HKCR\AppID subkeys with CLSID format and a non-empty display name.
/// </summary>
public partial class ComBrowserDialog : Form
{
    public string? SelectedAppId { get; private set; }

    private List<ComEntry> _all = [];

    private record ComEntry(string DisplayName, string AppId)
    {
        public override string ToString() => $"{DisplayName}  —  {AppId}";
    }

    /// <remarks>Synchronous load acceptable — HKCR\AppID enumeration is typically sub-second. Consider async with loading indicator if performance becomes an issue.</remarks>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Icon = AppIcons.GetAppIcon();
        _filterBox.TextChanged += (_, _) => ApplyFilter(_filterBox.Text);
        LoadRegistryEntries();
    }

    private void OnOkClick(object? sender, EventArgs e) => PickSelected();

    private void OnListDoubleClick(object? sender, EventArgs e)
    {
        if (_list.SelectedItem is ComEntry)
        {
            PickSelected();
            DialogResult = DialogResult.OK;
        }
    }

    private void PickSelected()
    {
        if (_list.SelectedItem is ComEntry entry)
            SelectedAppId = entry.AppId;
    }

    private void LoadRegistryEntries()
    {
        _all = [];
        try
        {
            using var appIdKey = Registry.ClassesRoot.OpenSubKey("AppID");
            if (appIdKey == null)
                return;

            foreach (var name in appIdKey.GetSubKeyNames())
            {
                if (!ClsidValidator.IsValid(name))
                    continue;
                using var sub = appIdKey.OpenSubKey(name);
                var displayName = sub?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;
                _all.Add(new ComEntry(displayName.Trim(), name));
            }

            _all.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
        }

        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string filter)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        var items = string.IsNullOrWhiteSpace(filter)
            ? _all
            : _all.Where(x =>
                x.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                x.AppId.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var item in items)
            _list.Items.Add(item);
        _list.EndUpdate();
    }
}
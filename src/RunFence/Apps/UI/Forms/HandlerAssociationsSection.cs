using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Inline handler association list editor for the AppEditDialog.
/// Shows this app's associations (filtered from effective mappings) with an editable Args Template column.
/// </summary>
public partial class HandlerAssociationsSection : UserControl
{
    private List<string> _loadedKeys = [];

    public event Action? Changed;

    /// <summary>
    /// When set, invoked to produce registry-suggested keys shown at the top of the Add dialog.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<IEnumerable<string>>? RegistrySuggestionFactory { get; set; }

    /// <summary>
    /// When set, called with the selected key to look up a registry-suggested arguments template
    /// for pre-populating the template field in the Add dialog.
    /// Return null to use <c>"%1"</c> as the default.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, string?>? RegistryTemplateLoader { get; set; }

    public HandlerAssociationsSection()
    {
        InitializeComponent();
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
    }

    public void SetAssociations(List<HandlerAssociationItem>? items)
    {
        _dataGrid.Rows.Clear();
        _loadedKeys = items?.Select(i => i.Key).ToList() ?? [];
        if (items == null)
            return;
        foreach (var item in items)
            _dataGrid.Rows.Add(item.Key, item.ArgumentsTemplate ?? "");
    }

    public List<HandlerAssociationItem>? GetAssociations()
    {
        _dataGrid.EndEdit();
        if (_dataGrid.Rows.Count == 0)
            return null;
        return _dataGrid.Rows.Cast<DataGridViewRow>()
            .Select(row =>
            {
                var key = row.Cells[0].Value?.ToString() ?? "";
                var template = row.Cells[1].Value?.ToString();
                template = string.IsNullOrEmpty(template) ? null : template;
                return new HandlerAssociationItem(key, template);
            })
            .ToList();
    }

    public void SetEnabled(bool enabled)
    {
        _dataGrid.Enabled = enabled;
        _addButton.Enabled = enabled;
        _removeButton.Enabled = enabled && _dataGrid.CurrentRow != null;
    }

    private IEnumerable<string> BuildSuggestions()
    {
        var fromRegistry = RegistrySuggestionFactory?.Invoke()?.ToList() ?? [];
        var currentKeys = _dataGrid.Rows.Cast<DataGridViewRow>()
            .Select(r => r.Cells[0].Value?.ToString() ?? "")
            .Where(k => k.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Include initially-loaded keys that have since been removed from the grid
        var removedLoadedKeys = _loadedKeys
            .Except(fromRegistry, StringComparer.OrdinalIgnoreCase)
            .Except(currentKeys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = fromRegistry
            .Except(currentKeys, StringComparer.OrdinalIgnoreCase)
            .Concat(removedLoadedKeys)
            .ToList();

        return primary.Concat(
            AppHandlerRegistrationService.CommonAssociationSuggestions
                .Except(primary, StringComparer.OrdinalIgnoreCase)
                .Except(currentKeys, StringComparer.OrdinalIgnoreCase));
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dlg = new AssociationKeyInputDialog("Add Association", BuildSuggestions());
        dlg.TemplateLoader = RegistryTemplateLoader;

        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        var key = dlg.SelectedKey;
        if (string.IsNullOrEmpty(key))
            return;

        if (!AppHandlerRegistrationService.IsValidKey(key))
        {
            MessageBox.Show("Invalid association key. Use a file extension (e.g., .pdf) or protocol name (e.g., http).",
                "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_dataGrid.Rows.Cast<DataGridViewRow>().Any(row =>
                string.Equals(row.Cells[0].Value?.ToString(), key, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This association is already in the list.",
                "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _dataGrid.Rows.Add(key, dlg.SelectedTemplate ?? "");
        Changed?.Invoke();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_dataGrid.CurrentRow != null)
        {
            _dataGrid.Rows.Remove(_dataGrid.CurrentRow);
            Changed?.Invoke();
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _removeButton.Enabled = _dataGrid.Enabled && _dataGrid.CurrentRow != null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _dataGrid.CurrentRow != null && !_dataGrid.IsCurrentCellInEditMode)
            OnRemoveClick(sender, e);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var hit = _dataGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0)
                _dataGrid.Rows[hit.RowIndex].Selected = true;
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (!_dataGrid.Enabled)
        {
            e.Cancel = true;
            return;
        }

        _ctxAdd.Visible = _dataGrid.CurrentRow == null;
        _ctxRemove.Visible = _dataGrid.CurrentRow != null;
    }
}

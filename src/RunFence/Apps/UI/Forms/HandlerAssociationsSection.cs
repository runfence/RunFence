using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Inline handler association list editor for the AppEditDialog.
/// Shows this app's associations (filtered from effective mappings) with an editable Args Template column.
/// </summary>
public partial class HandlerAssociationsSection : UserControl
{
    private readonly record struct AssociationRowData(List<string>? Prefixes, bool ReplacePrefixes);

    private readonly IExeAssociationRegistryReader? _reader;
    private List<string> _loadedKeys = [];
    private int _ctxRowIndex = -1;

    public event Action? Changed;

    /// <summary>The exe path used for registry suggestions. Set by AppEditDialog when the path changes.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ExePath { get; set; } = "";

    public HandlerAssociationsSection()
    {
        InitializeComponent();
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _editButton.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxAdd.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _ctxEdit.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _dataGrid.CellDoubleClick += OnCellDoubleClick;
    }

    public HandlerAssociationsSection(IExeAssociationRegistryReader reader) : this()
    {
        _reader = reader;
    }

    public void SetAssociations(List<HandlerAssociationItem>? items)
    {
        _dataGrid.Rows.Clear();
        _loadedKeys = items?.Select(i => i.Key).ToList() ?? [];
        if (items == null)
            return;
        foreach (var item in items)
        {
            var idx = _dataGrid.Rows.Add(item.Key, item.ArgumentsTemplate ?? "");
            _dataGrid.Rows[idx].Tag = new AssociationRowData(item.PathPrefixes?.ToList(), item.ReplacePrefixes);
        }
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
                var data = row.Tag is AssociationRowData d ? d : default;
                var prefixes = data.Prefixes;
                var replace = data.ReplacePrefixes;
                return new HandlerAssociationItem(key, template,
                    prefixes?.Count > 0 ? (IReadOnlyList<string>)prefixes : null, replace);
            })
            .ToList();
    }

    public void SetEnabled(bool enabled)
    {
        _dataGrid.Enabled = enabled;
        _addButton.Enabled = enabled;
        _editButton.Enabled = enabled && _dataGrid.CurrentRow != null;
        _removeButton.Enabled = enabled && _dataGrid.CurrentRow != null;
    }

    private IEnumerable<string> BuildSuggestions()
    {
        var fromRegistry = _reader?.GetHandledAssociations(ExePath).ToList() ?? [];
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
        if (_reader == null)
            return;
        using var dlg = new HandlerAssociationEditDialog();
        dlg.InitializeForAdd(BuildSuggestions(), _reader, ExePath);

        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        var key = dlg.SelectedKey;
        if (string.IsNullOrEmpty(key))
            return;

        if (_dataGrid.Rows.Cast<DataGridViewRow>().Any(row =>
                string.Equals(row.Cells[0].Value?.ToString(), key, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This association is already in the list.",
                "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var idx = _dataGrid.Rows.Add(key, dlg.NewTemplate ?? "");
        _dataGrid.Rows[idx].Tag = new AssociationRowData(dlg.NewPrefixes?.ToList(), dlg.NewReplacePrefixes);
        Changed?.Invoke();
    }

    private void OnEditClick(object? sender, EventArgs e)
    {
        var row = GetActionRow(sender);
        if (row == null)
            return;

        var key = row.Cells[0].Value?.ToString() ?? "";
        var template = row.Cells[1].Value?.ToString();
        var data = row.Tag is AssociationRowData d ? d : default;
        var prefixes = data.Prefixes;
        var replace = data.ReplacePrefixes;

        using var dlg = new HandlerAssociationEditDialog();
        dlg.Initialize(key, template, prefixes, replace);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        row.Cells[1].Value = dlg.NewTemplate ?? "";
        row.Tag = new AssociationRowData(dlg.NewPrefixes?.ToList(), dlg.NewReplacePrefixes);
        Changed?.Invoke();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        var row = GetActionRow(sender);
        if (row != null)
        {
            _dataGrid.Rows.Remove(row);
            Changed?.Invoke();
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _editButton.Enabled = _dataGrid.Enabled && _dataGrid.CurrentRow != null;
        _removeButton.Enabled = _dataGrid.Enabled && _dataGrid.CurrentRow != null;
    }

    private void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
            OnEditClick(null, EventArgs.Empty);
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
            {
                _ctxRowIndex = hit.RowIndex;
                _dataGrid.ClearSelection();
                _dataGrid.Rows[hit.RowIndex].Selected = true;
                var cellIndex = hit.ColumnIndex >= 0 ? hit.ColumnIndex : 0;
                _dataGrid.CurrentCell = _dataGrid.Rows[hit.RowIndex].Cells[cellIndex];
            }
            else
            {
                _ctxRowIndex = -1;
                _dataGrid.ClearSelection();
                _dataGrid.CurrentCell = null;
            }
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (!_dataGrid.Enabled)
        {
            e.Cancel = true;
            return;
        }

        if (_ctxRowIndex < 0)
        {
            _dataGrid.ClearSelection();
            _dataGrid.CurrentCell = null;
        }

        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxEdit.Visible = _ctxRowIndex >= 0;
        _ctxRemove.Visible = _ctxRowIndex >= 0;
    }

    private DataGridViewRow? GetActionRow(object? sender)
    {
        if (sender is ToolStripItem && _ctxRowIndex >= 0 && _ctxRowIndex < _dataGrid.Rows.Count)
            return _dataGrid.Rows[_ctxRowIndex];

        return _dataGrid.CurrentRow;
    }
}

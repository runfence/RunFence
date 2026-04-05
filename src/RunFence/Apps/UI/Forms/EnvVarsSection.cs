using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Reusable environment-variables editor showing a key/value DataGridView with add/remove toolbar.
/// </summary>
public partial class EnvVarsSection : UserControl
{
    private int _ctxRowIndex = -1;

    public EnvVarsSection()
    {
        InitializeComponent();
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxAdd.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
    }

    public void SetItems(Dictionary<string, string>? vars)
    {
        _dataGrid.Rows.Clear();
        if (vars == null)
            return;
        foreach (var kv in vars)
            _dataGrid.Rows.Add(kv.Key, kv.Value);
    }

    /// <summary>
    /// Returns the current env vars, or null if empty. Duplicate names (case-insensitive) are collapsed
    /// to the last value — callers should validate before calling.
    /// </summary>
    public Dictionary<string, string>? GetItems()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _dataGrid.Rows)
        {
            if (row.IsNewRow)
                continue;
            var name = row.Cells[0].Value as string;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var value = row.Cells[1].Value as string ?? string.Empty;
            result[name.Trim()] = value;
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Returns duplicate name entries (case-insensitive) for validation, or null if none.
    /// </summary>
    public string? GetFirstDuplicateName()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (from DataGridViewRow row in _dataGrid.Rows
            where !row.IsNewRow
            select row.Cells[0].Value as string
            into name
            where !string.IsNullOrWhiteSpace(name)
            select name.Trim())
            .FirstOrDefault(trimmed => !seen.Add(trimmed));
    }

    public void SetEnabled(bool enabled)
    {
        _dataGrid.Enabled = enabled;
        _addButton.Enabled = enabled;
        _removeButton.Enabled = enabled && _dataGrid.CurrentRow is { IsNewRow: false };
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        _removeButton.Enabled = _dataGrid.Enabled && _dataGrid.CurrentRow is { IsNewRow: false };
    }

    private void OnGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;
        var hit = _dataGrid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0 && hit.RowIndex != _dataGrid.NewRowIndex)
        {
            _dataGrid.CurrentCell = _dataGrid.Rows[hit.RowIndex].Cells[0];
            _ctxRowIndex = hit.RowIndex;
        }
        else
        {
            _ctxRowIndex = -1;
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (!_dataGrid.Enabled)
        {
            e.Cancel = true;
            return;
        }

        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxRemove.Visible = _ctxRowIndex >= 0;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        if (!_dataGrid.Enabled)
            return;
        _dataGrid.CurrentCell = _dataGrid.Rows[_dataGrid.NewRowIndex].Cells[0];
        _dataGrid.BeginEdit(true);
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_dataGrid.CurrentRow == null || _dataGrid.CurrentRow.IsNewRow)
            return;
        _dataGrid.Rows.RemoveAt(_dataGrid.CurrentRow.Index);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dataGrid.IsCurrentCellInEditMode)
            return;
        if (e.KeyCode == Keys.Delete)
            OnRemoveClick(sender, e);
    }
}
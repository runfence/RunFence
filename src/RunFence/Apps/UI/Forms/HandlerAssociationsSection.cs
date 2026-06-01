using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Inline handler association list editor for the AppEditDialog.
/// Shows this app's associations (filtered from effective mappings) with an editable Args Template column.
/// </summary>
public partial class HandlerAssociationsSection : UserControl
{
    private readonly record struct AssociationRowData(CombinedPrefixesState PrefixState);

    private IHandlerAssociationMutationService? _mutationService;
    private HandlerAssociationsChildDialogCoordinator? _childDialogCoordinator;
    private IUiIconService? _iconService;
    private IHandlerAssociationsHost? _host;
    private List<string> _loadedKeys = [];
    private int _ctxRowIndex = -1;
    private bool _isInitialized;

    public event Action? Changed;

    /// <summary>The exe path used for registry suggestions. Set by AppEditDialog when the path changes.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ExePath { get; set; } = "";

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? AccountSid { get; set; }

    public void Initialize(
        IHandlerAssociationMutationService mutationService,
        HandlerAssociationsChildDialogCoordinator childDialogCoordinator,
        IUiIconService iconService,
        IHandlerAssociationsHost host)
    {
        ArgumentNullException.ThrowIfNull(mutationService);
        ArgumentNullException.ThrowIfNull(childDialogCoordinator);
        ArgumentNullException.ThrowIfNull(iconService);
        ArgumentNullException.ThrowIfNull(host);

        _mutationService = mutationService;
        _childDialogCoordinator = childDialogCoordinator;
        _iconService = iconService;
        _host = host;
        ApplyRuntimeIcons();
        _isInitialized = true;
        OnSelectionChanged(this, EventArgs.Empty);
        _addButton.Enabled = _dataGrid.Enabled;
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
            _dataGrid.Rows[idx].Tag = new AssociationRowData(CreatePrefixState(item));
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
                var state = row.Tag is AssociationRowData data
                    ? data.PrefixState
                    : new CombinedPrefixesState(null, null, false);
                return new HandlerAssociationItem(key, template,
                    state.AssociationPrefixes?.Count > 0 ? state.AssociationPrefixes : null,
                    state.ReplacePrefixes);
            })
            .ToList();
    }

    public void SetEnabled(bool enabled)
    {
        if (!_isInitialized)
            return;

        _dataGrid.Enabled = enabled;
        _addButton.Enabled = enabled;
        _editButton.Enabled = enabled && _dataGrid.CurrentRow != null;
        _removeButton.Enabled = enabled && _dataGrid.CurrentRow != null;
    }

    private IEnumerable<string> BuildSuggestions()
    {
        if (!_isInitialized || _mutationService == null)
            return [];

        var currentKeys = _dataGrid.Rows.Cast<DataGridViewRow>()
            .Select(r => r.Cells[0].Value?.ToString() ?? "")
            .ToList();
        var exePath = !string.IsNullOrWhiteSpace(ExePath)
            ? ExePath
            : _host?.GetSelectedApp()?.ExePath ?? string.Empty;
        var mode = _host?.GetCurrentAssociationMode() ?? HandlerAssociationMode.App;
        return _mutationService.BuildSuggestions(exePath, AccountSid, _loadedKeys, currentKeys, mode);
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        if (!_isInitialized || _childDialogCoordinator == null)
            return;

        var currentKeys = _dataGrid.Rows.Cast<DataGridViewRow>()
            .Select(row => row.Cells[0].Value?.ToString() ?? string.Empty)
            .Where(key => key.Length > 0)
            .ToList();
        var result = _childDialogCoordinator.ShowAddDialog(
            FindForm(),
            BuildSuggestions().ToList(),
            !string.IsNullOrWhiteSpace(ExePath) ? ExePath : _host?.GetSelectedApp()?.ExePath ?? string.Empty,
            AccountSid,
            currentKeys);
        if (result == null)
            return;

        AddAssociationRow(result.Value);
        RaiseChanged();
    }

    private void OnEditClick(object? sender, EventArgs e)
    {
        if (!_isInitialized || _childDialogCoordinator == null)
            return;

        var row = GetActionRow(sender);
        if (row == null)
            return;

        var state = row.Tag is AssociationRowData data
            ? data.PrefixState
            : new CombinedPrefixesState(null, null, false);
        var existing = new HandlerAssociationItem(
            row.Cells[0].Value?.ToString() ?? string.Empty,
            row.Cells[1].Value?.ToString(),
            state.AssociationPrefixes,
            state.ReplacePrefixes);
        var result = _childDialogCoordinator.ShowEditDialog(FindForm(), existing);
        if (result == null)
            return;

        row.Cells[1].Value = result.Value.ArgumentsTemplate ?? "";
        row.Tag = new AssociationRowData(CreatePrefixState(result.Value));
        RaiseChanged();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        var row = GetActionRow(sender);
        if (row != null)
        {
            _dataGrid.Rows.Remove(row);
            RaiseChanged();
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
        UpdateContextTarget(e);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        UpdateContextTarget(e);
    }

    private void UpdateContextTarget(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var hit = _dataGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0 && IsRightClickWithinCellBounds(hit.ColumnIndex, hit.RowIndex, e.Location))
            {
                _ctxRowIndex = hit.RowIndex;
                _dataGrid.ClearSelection();
                _dataGrid.Rows[hit.RowIndex].Selected = true;
                _dataGrid.CurrentCell = _dataGrid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
            }
            else
            {
                _ctxRowIndex = -1;
                _dataGrid.ClearSelection();
                _dataGrid.CurrentCell = null;
            }
        }
    }

    private bool IsRightClickWithinCellBounds(int columnIndex, int rowIndex, Point location)
    {
        var cellBounds = _dataGrid.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: false);
        return cellBounds.Contains(location);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (!_dataGrid.Enabled)
        {
            e.Cancel = true;
            return;
        }

        var cursorLocation = _dataGrid.PointToClient(Cursor.Position);
        var hit = _dataGrid.HitTest(cursorLocation.X, cursorLocation.Y);
        if (hit.RowIndex < 0 || hit.ColumnIndex < 0 || !IsRightClickWithinCellBounds(hit.ColumnIndex, hit.RowIndex, cursorLocation))
            _ctxRowIndex = -1;

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

    private void AddAssociationRow(HandlerAssociationItem item)
    {
        var index = _dataGrid.Rows.Add(item.Key, item.ArgumentsTemplate ?? "");
        _dataGrid.Rows[index].Tag = new AssociationRowData(CreatePrefixState(item));
    }

    private static CombinedPrefixesState CreatePrefixState(HandlerAssociationItem item)
        => new(
            AppPrefixes: null,
            AssociationPrefixes: item.PathPrefixes?.ToList(),
            ReplacePrefixes: item.ReplacePrefixes);

    private void RaiseChanged()
    {
        Changed?.Invoke();
        _host?.RefreshMappings();
    }

    private void ApplyRuntimeIcons()
    {
        if (_iconService == null)
            return;

        _addButton.Image = _iconService.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _editButton.Image = _iconService.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99));
        _removeButton.Image = _iconService.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxAdd.Image = _iconService.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _ctxEdit.Image = _iconService.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _ctxRemove.Image = _iconService.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
    }
}

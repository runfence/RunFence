using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Reusable flat path prefix list editor: GroupBox with a ToolStrip (folder +, manual +, −) and a single-column
/// editable DataGridView. The folder + button opens a FolderBrowserDialog (Cancel does nothing).
/// The manual + button adds an empty editable row for manual entry (e.g. URL schemes like <c>https://internal.</c>).
/// Filesystem paths are normalized to have a trailing backslash on retrieval.
/// </summary>
public partial class PathPrefixesSection : PrefixListBase
{
    protected override void OnCreateControl()
    {
        EnsurePrefixListRuntimeInitialized();
        base.OnCreateControl();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string GroupBoxTitle
    {
        get => _contentGroup.Text;
        set => _contentGroup.Text = value;
    }

    /// <summary>
    /// Registers a tooltip on the group box header so it is visible when hovering over the section title area.
    /// Pass <c>null</c> for <paramref name="text"/> to clear the tooltip.
    /// </summary>
    public void SetGroupBoxTooltip(ToolTip tooltip, string? text) =>
        tooltip.SetToolTip(_contentGroup, text);

    public void SetPrefixes(IReadOnlyList<string>? prefixes)
    {
        EnsurePrefixListRuntimeInitialized();
        _dataGrid.Rows.Clear();
        if (prefixes == null) return;
        foreach (var prefix in prefixes)
            _dataGrid.Rows.Add(prefix);
    }

    public IReadOnlyList<string>? GetPrefixes()
    {
        EnsurePrefixListRuntimeInitialized();
        _dataGrid.EndEdit();
        var values = _dataGrid.Rows.Cast<DataGridViewRow>()
            .Select(r => r.Cells[0].Value?.ToString()?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => PathPrefixHelper.NormalizePath(v!))
            .ToList();
        return values.Count > 0 ? values : null;
    }

    public void SetEnabled(bool enabled)
    {
        EnsurePrefixListRuntimeInitialized();
        _dataGrid.Enabled = enabled;
        _addButton.Enabled = enabled;
        _addManualButton.Enabled = enabled;
        _removeButton.Enabled = enabled && _dataGrid.CurrentRow != null;
    }

    protected override void PerformAdd(string path)
    {
        EnsurePrefixListRuntimeInitialized();
        _dataGrid.Rows.Add(path);
    }

    protected override void PerformAddManual()
    {
        EnsurePrefixListRuntimeInitialized();
        var idx = _dataGrid.Rows.Add("");
        _dataGrid.CurrentCell = _dataGrid.Rows[idx].Cells[0];
        _dataGrid.BeginEdit(true);
    }

    protected override void UpdateRemoveButton()
    {
        EnsurePrefixListRuntimeInitialized();
        _removeButton.Enabled = _dataGrid.Enabled && _dataGrid.CurrentRow != null;
    }

    protected override void SetupContextMenu()
    {
        EnsurePrefixListRuntimeInitialized();
        _ctxAdd.Visible = _dataGrid.CurrentRow == null;
        _ctxAddManual.Visible = _dataGrid.CurrentRow == null;
        _ctxRemove.Visible = _dataGrid.CurrentRow != null;
    }
}

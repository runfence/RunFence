namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Sectioned "Path Prefixes" list with app-level and association-level entries shown as header-separated sections,
/// plus Add/Replace radio buttons hosted in the toolbar.
/// <para>
/// Call <see cref="EnableFlatMode"/> before use to switch to a flat list showing only the association-override
/// entries (no section headers, no app-level section). Outside flat mode, selecting "Replace" temporarily uses
/// the same association-only presentation while preserving the hidden app rows.
/// </para>
/// <para>
/// Used by handler-association dialogs that need to edit app-level prefixes together with per-association overrides.
/// </para>
/// </summary>
public partial class CombinedPrefixesSection : PrefixListBase
{
    private sealed record AppPrefixHeader;
    private sealed record AssocPrefixHeader;

    private static readonly Color HeaderBackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
    private static readonly Color HeaderForeColor = Color.FromArgb(0x22, 0x22, 0x66);

    private bool _flatMode;

    protected override void OnRuntimeInitialize()
    {
        AddSectionHeader("App", isApp: true);
        AddSectionHeader("Association override", isApp: false);
        _addRadio.CheckedChanged += OnModeChanged;
        _replaceRadio.CheckedChanged += OnModeChanged;
        UpdateSectionVisibility();
    }

    protected override void OnCreateControl()
    {
        EnsurePrefixListRuntimeInitialized();
        base.OnCreateControl();
    }

    /// <summary>
    /// Switches to flat mode: hides both section headers and the app section entirely.
    /// Must be called once before any data is loaded.
    /// </summary>
    public void EnableFlatMode()
    {
        EnsurePrefixListRuntimeInitialized();
        _flatMode = true;
        UpdateSectionVisibility();
    }

    /// <summary>Loads the app-level prefixes into the "App" section. No-op in flat mode.</summary>
    public void SetAppPrefixes(IReadOnlyList<string>? prefixes)
    {
        EnsurePrefixListRuntimeInitialized();
        if (_flatMode) return;
        RefillSection(isApp: true, prefixes);
        UpdateSectionVisibility();
    }

    /// <summary>Loads the per-association prefix overrides and the Add/Replace radio state.</summary>
    public void SetAssociationPrefixes(IReadOnlyList<string>? prefixes, bool replacePrefixes)
    {
        EnsurePrefixListRuntimeInitialized();
        if (replacePrefixes)
            _replaceRadio.Checked = true;
        else
            _addRadio.Checked = true;

        RefillSection(isApp: false, prefixes);
        UpdateSectionVisibility();
    }

    /// <summary>Returns the current app-level prefixes, or null if empty.</summary>
    public IReadOnlyList<string>? GetAppPrefixes()
    {
        EnsurePrefixListRuntimeInitialized();
        return CollectSectionPrefixes(isApp: true);
    }

    /// <summary>Returns the current per-association prefix overrides, or null if empty.</summary>
    public IReadOnlyList<string>? GetAssociationPrefixes()
    {
        EnsurePrefixListRuntimeInitialized();
        return CollectSectionPrefixes(isApp: false);
    }

    /// <summary>True when "Replace" is selected; false when "Add (union)" is selected.</summary>
    public bool IsReplace
    {
        get
        {
            EnsurePrefixListRuntimeInitialized();
            return _replaceRadio.Checked;
        }
    }

    public void RegisterContextHelp(RunFence.UI.Forms.ContextHelpForm host)
    {
        host.SetContextHelp(_contentGroup, RunFence.UI.Forms.ContextHelpTextCatalog.App_PathPrefixes);
    }

    protected override void PerformAdd(string path)
    {
        EnsurePrefixListRuntimeInitialized();
        AddToSection(path, false);
    }

    protected override void PerformAddManual()
    {
        EnsurePrefixListRuntimeInitialized();
        AddToSection("", true);
    }

    protected override bool CanRemoveCurrentRow()
    {
        EnsurePrefixListRuntimeInitialized();
        var row = _dataGrid.CurrentRow;
        return row != null && row.Tag is not AppPrefixHeader and not AssocPrefixHeader;
    }

    protected override void PerformRemove()
    {
        EnsurePrefixListRuntimeInitialized();
        var row = _dataGrid.CurrentRow;
        if (row == null || row.Tag is AppPrefixHeader or AssocPrefixHeader) return;
        _dataGrid.Rows.Remove(row);
    }

    protected override void SetupContextMenu()
    {
        EnsurePrefixListRuntimeInitialized();
        _ctxAdd.Visible = true;
        _ctxAddManual.Visible = true;
        _ctxRemove.Visible = CanRemoveCurrentRow();
    }

    private void OnModeChanged(object? sender, EventArgs e) => UpdateSectionVisibility();

    private void AddSectionHeader(string title, bool isApp)
    {
        _headerFont ??= new Font(_dataGrid.Font, FontStyle.Bold);
        var idx = _dataGrid.Rows.Add(title);
        var row = _dataGrid.Rows[idx];
        if (isApp) row.Tag = new AppPrefixHeader();
        else row.Tag = new AssocPrefixHeader();
        row.DefaultCellStyle.BackColor = HeaderBackColor;
        row.DefaultCellStyle.SelectionBackColor = HeaderBackColor;
        row.DefaultCellStyle.SelectionForeColor = Color.Black;
        row.DefaultCellStyle.Font = _headerFont;
        row.DefaultCellStyle.ForeColor = HeaderForeColor;
        row.ReadOnly = true;
    }

    private void UpdateSectionVisibility()
    {
        var appHeaderIdx = FindHeaderIndex(true);
        var assocHeaderIdx = FindHeaderIndex(false);
        if (appHeaderIdx < 0 || assocHeaderIdx < 0)
            return;

        var associationOnly = _flatMode || _replaceRadio.Checked;
        SafeMoveCellBeforeHiding(appHeaderIdx, assocHeaderIdx, !associationOnly);
        SetSectionRowsVisible(appHeaderIdx, assocHeaderIdx, !associationOnly);
        SetSectionRowsVisible(assocHeaderIdx, _dataGrid.Rows.Count, true, headerVisible: !associationOnly);
        UpdateRemoveButton();
    }

    /// <summary>
    /// Before hiding the app section and association header, moves <see cref="DataGridView.CurrentCell"/>
    /// to the first visible editable association row, or clears the selection when none exists, so that
    /// WinForms never hides the row that owns the current cell.
    /// </summary>
    private void SafeMoveCellBeforeHiding(int appHeaderIdx, int assocHeaderIdx, bool appSectionVisible)
    {
        if (appSectionVisible) return;

        var currentCell = _dataGrid.CurrentCell;
        if (currentCell == null) return;

        var currentRowIdx = currentCell.RowIndex;
        if (currentRowIdx < 0) return;

        // Both app section rows AND the assoc header row are hidden when switching to association-only mode.
        var rowWillBeHidden = currentRowIdx >= appHeaderIdx && currentRowIdx <= assocHeaderIdx;

        if (!rowWillBeHidden) return;

        // Find the first editable visible association row (after the assoc header).
        for (var i = assocHeaderIdx + 1; i < _dataGrid.Rows.Count; i++)
        {
            var row = _dataGrid.Rows[i];
            if (row.Tag is AppPrefixHeader or AssocPrefixHeader) continue;
            if (!row.Visible) continue;
            _dataGrid.CurrentCell = row.Cells[0];
            return;
        }

        _dataGrid.ClearSelection();
        _dataGrid.CurrentCell = null;
    }

    private void SetSectionRowsVisible(int headerIdx, int endIdx, bool rowsVisible, bool? headerVisible = null)
    {
        _dataGrid.Rows[headerIdx].Visible = headerVisible ?? rowsVisible;

        for (var i = headerIdx + 1; i < endIdx; i++)
            _dataGrid.Rows[i].Visible = rowsVisible;
    }

    private void RefillSection(bool isApp, IReadOnlyList<string>? prefixes)
    {
        var headerIdx = FindHeaderIndex(isApp);
        if (headerIdx < 0) return;

        var endIdx = GetSectionEnd(headerIdx);
        for (var i = endIdx - 1; i > headerIdx; i--)
            _dataGrid.Rows.RemoveAt(i);

        if (prefixes != null)
        {
            var insertAt = headerIdx + 1;
            foreach (var p in prefixes)
            {
                _dataGrid.Rows.Insert(insertAt, p);
                insertAt++;
            }
        }
    }

    private int FindHeaderIndex(bool isApp)
    {
        for (var i = 0; i < _dataGrid.Rows.Count; i++)
        {
            if (isApp && _dataGrid.Rows[i].Tag is AppPrefixHeader) return i;
            if (!isApp && _dataGrid.Rows[i].Tag is AssocPrefixHeader) return i;
        }
        return -1;
    }

    private int GetSectionEnd(int headerIdx)
    {
        for (var i = headerIdx + 1; i < _dataGrid.Rows.Count; i++)
        {
            if (_dataGrid.Rows[i].Tag is AppPrefixHeader or AssocPrefixHeader)
                return i;
        }
        return _dataGrid.Rows.Count;
    }

    private bool IsInAppSection(int rowIndex)
    {
        for (var i = rowIndex; i >= 0; i--)
        {
            if (_dataGrid.Rows[i].Tag is AppPrefixHeader) return true;
            if (_dataGrid.Rows[i].Tag is AssocPrefixHeader) return false;
        }
        return false;
    }

    private (bool isApp, int headerIdx) GetAddTarget()
    {
        var row = _dataGrid.CurrentRow;
        if (row == null || !row.Visible)
            return (false, FindHeaderIndex(false));
        if (row.Tag is AppPrefixHeader)
            return (true, row.Index);
        if (row.Tag is AssocPrefixHeader)
            return (false, row.Index);
        var inApp = !_flatMode && IsInAppSection(row.Index);
        return (inApp, FindHeaderIndex(inApp));
    }

    private void AddToSection(string value, bool beginEdit)
    {
        var (_, headerIdx) = GetAddTarget();
        if (headerIdx < 0) return;

        var insertIdx = GetSectionEnd(headerIdx);
        _dataGrid.Rows.Insert(insertIdx, value);

        if (beginEdit)
        {
            _dataGrid.CurrentCell = _dataGrid.Rows[insertIdx].Cells[0];
            _dataGrid.BeginEdit(true);
        }
    }

    private IReadOnlyList<string>? CollectSectionPrefixes(bool isApp)
    {
        _dataGrid.EndEdit();
        var inSection = false;
        var result = new List<string>();

        foreach (DataGridViewRow row in _dataGrid.Rows)
        {
            if (row.Tag is AppPrefixHeader) { inSection = isApp; continue; }
            if (row.Tag is AssocPrefixHeader) { inSection = !isApp; continue; }
            if (!inSection) continue;

            var v = row.Cells[0].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(v)) continue;
            result.Add(PathPrefixHelper.NormalizePath(v!));
        }

        return result.Count > 0 ? result : null;
    }
}

using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;

namespace RunFence.UI.Forms;

public class DataPanel : UserControl
{
    protected SessionContext Session { get; private set; } = null!;

    protected AppDatabase Database => Session.Database;
    protected CredentialStore CredentialStore => Session.CredentialStore;
    protected ProtectedBuffer PinDerivedKey => Session.PinDerivedKey;

    // --- Modal tracking (thread-safe counter) ---
    // Panels increment when opening a modal dialog, decrement on close.
    // MainForm.IsModalOpen reads this to block availability checks and IPC config commands.
    // Static accessor delegates to the IModalTracker singleton set during startup.
    private static IModalTracker _modalTracker = new ModalTracker();
    private static readonly ISecureDesktopRunner _secureDesktopRunner = new SecureDesktopHelper();

    /// <summary>
    /// Sets the modal tracker singleton. Called once during DI startup so all panels share one tracker.
    /// </summary>
    public static void SetModalTracker(IModalTracker tracker) => _modalTracker = tracker;

    /// <summary>
    /// Signal that a modal dialog is about to open. Must be paired with EndModal() in a finally block.
    /// </summary>
    public static void BeginModal() => _modalTracker.BeginModal();

    /// <summary>Signal that a modal dialog was closed.</summary>
    public static void EndModal() => _modalTracker.EndModal();

    /// <summary>
    /// Shows a modal dialog and wraps it with BeginModal/EndModal tracking.
    /// </summary>
    protected DialogResult ShowModalDialog(Form dialog) => ShowModalDialog(dialog, FindForm());

    /// <summary>
    /// Shows a modal dialog with an explicit owner, wrapped with BeginModal/EndModal tracking.
    /// For use by non-DataPanel classes that need modal tracking.
    /// </summary>
    public static DialogResult ShowModal(Form dialog, IWin32Window? owner)
    {
        BeginModal();
        try
        {
            return dialog.ShowDialog(owner);
        }
        finally
        {
            EndModal();
        }
    }

    /// <summary>
    /// Runs an action on the secure desktop with BeginModal/EndModal tracking.
    /// Use for dialogs that accept sensitive input (passwords, credentials).
    /// </summary>
    public static void RunOnSecureDesktop(Action action)
    {
        BeginModal();
        try
        {
            _secureDesktopRunner.Run(action);
        }
        finally
        {
            EndModal();
        }
    }

    /// <summary>
    /// Shows a modal dialog with an explicit owner, wrapped with BeginModal/EndModal tracking.
    /// </summary>
    protected static DialogResult ShowModalDialog(Form dialog, IWin32Window? owner)
        => ShowModal(dialog, owner);

    public void SetData(SessionContext session)
    {
        Session = session;
        OnDataSet();
    }

    protected virtual void OnDataSet()
    {
    }

    /// <summary>
    /// Called when the app window gains focus, is restored from minimized, or this panel's tab is activated.
    /// Override to perform a lightweight data refresh for externally-changing data.
    /// </summary>
    public virtual void RefreshOnActivation()
    {
    }

    // --- Grid duplication helpers ---

    /// <summary>
    /// Tracks whether the grid is being repopulated. Subclasses set this in
    /// their RefreshGrid methods to suppress spurious SelectionChanged events.
    /// </summary>
    protected bool IsRefreshing { get; set; }

    /// <summary>
    /// Called when grid selection changes (and not refreshing). Subclasses
    /// override to enable/disable buttons based on the current selection.
    /// </summary>
    protected virtual void UpdateButtonState()
    {
    }

    /// <summary>
    /// Standard SelectionChanged handler: skips during refresh, otherwise
    /// delegates to <see cref="UpdateButtonState"/>.
    /// </summary>
    protected void OnGridSelectionChanged(object? sender, EventArgs e)
    {
        if (IsRefreshing)
            return;
        UpdateButtonState();
    }

    /// <summary>
    /// Configures a DataGridView with standard read-only behavioral settings.
    /// Visual styling is provided by <see cref="UI.Controls.StyledDataGridView"/>.
    /// </summary>
    public static void ConfigureReadOnlyGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    /// <summary>
    /// Returns the index of the first non-image column cell suitable for CurrentCell assignment.
    /// </summary>
    private static int GetFirstTextCellIndex(DataGridView grid)
    {
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            if (grid.Columns[i] is not DataGridViewImageColumn && grid.Columns[i].Visible)
                return i;
        }

        return 0;
    }

    // --- Three-state column sorting ---

    protected readonly GridSortHelper _sortHelper = new();

    /// <summary>True when a column sort is active (Ascending or Descending).</summary>
    protected bool IsSortActive => _sortHelper.IsSortActive;

    /// <summary>The index of the currently sorted column, or -1 if no sort is active.</summary>
    protected int SortColumnIndex => _sortHelper.SortColumnIndex;

    /// <summary>The current sort direction.</summary>
    protected SortOrder SortDirection => _sortHelper.SortDirection;

    /// <inheritdoc cref="GridSortHelper.EnableThreeStateSorting"/>
    protected void EnableThreeStateSorting(DataGridView grid, Action restoreOriginalOrder,
        Action? onEnterSort = null, bool sectioned = false)
        => _sortHelper.EnableThreeStateSorting(grid, restoreOriginalOrder, onEnterSort, sectioned);

    /// <inheritdoc cref="GridSortHelper.ReapplyGlyphIfActive"/>
    protected void ReapplyGlyphIfActive(DataGridView grid)
        => _sortHelper.ReapplyGlyphIfActive(grid);

    /// <inheritdoc cref="GridSortHelper.SortByActiveColumn{T}"/>
    protected IOrderedEnumerable<T> SortByActiveColumn<T>(
        IEnumerable<T> items, Func<T, string> keySelector)
        => _sortHelper.SortByActiveColumn(items, keySelector);

    protected static void SelectFirstRow(DataGridView grid)
    {
        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[GetFirstTextCellIndex(grid)];
        }
    }

    protected static void SelectRowByIndex(DataGridView grid, int index)
    {
        if (grid.Rows.Count == 0)
            return;
        var targetIndex = Math.Min(index, grid.Rows.Count - 1);
        grid.Rows[targetIndex].Selected = true;
        grid.CurrentCell = grid.Rows[targetIndex].Cells[GetFirstTextCellIndex(grid)];
    }

    protected static void ShowContextMenuAtRow(DataGridView grid, ContextMenuStrip menu)
    {
        if (grid.SelectedRows.Count == 0)
            return;
        var row = grid.SelectedRows[0];
        var cellBounds = grid.GetCellDisplayRectangle(1, row.Index, true);
        menu.Show(grid, new Point(cellBounds.Left + cellBounds.Width / 2, cellBounds.Top + cellBounds.Height / 2));
    }

    protected static void HandleRightClickRowSelect(DataGridView grid, DataGridViewCellMouseEventArgs e, ContextMenuStrip contextMenu)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;
        grid.ClearSelection();
        grid.Rows[e.RowIndex].Selected = true;
        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[GetFirstTextCellIndex(grid)];
        contextMenu.Show(grid, grid.PointToClient(Cursor.Position));
    }
}
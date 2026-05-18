using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.UI.Forms;

public class DataPanel : UserControl
{
    private readonly IModalCoordinator _modalCoordinator = null!;

    /// <summary>
    /// DI constructor. Pass <see cref="IModalCoordinator"/> for runtime use.
    /// </summary>
    protected DataPanel(IModalCoordinator modalCoordinator)
    {
        _modalCoordinator = modalCoordinator;
    }

    /// <summary>
    /// Parameterless constructor for the WinForms Designer. Not used at runtime.
    /// </summary>
    protected DataPanel()
    {
    }

    protected override Size DefaultSize => new Size(900, 600);

    protected SessionContext Session { get; private set; } = null!;

    protected AppDatabase Database => Session.Database;
    protected CredentialStore CredentialStore => Session.CredentialStore;

    /// <summary>
    /// Shows a modal dialog and wraps it with BeginModal/EndModal tracking.
    /// </summary>
    protected DialogResult ShowModalDialog(Form dialog) => ShowModalDialog(dialog, FindForm());

    /// <summary>
    /// Shows a modal dialog with an explicit owner, wrapped with BeginModal/EndModal tracking.
    /// </summary>
    protected DialogResult ShowModalDialog(Form dialog, IWin32Window? owner)
        => _modalCoordinator.ShowModal(dialog, owner);

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

    // --- Grid refresh state and selection helpers ---

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

}

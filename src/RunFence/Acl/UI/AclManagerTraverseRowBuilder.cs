using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Builds DataGridView rows for the traverse tab in <see cref="AclManagerDialog"/>.
/// Separates tracked-path row building (entries with <see cref="GrantedPathEntry.AllAppliedPaths"/>)
/// from legacy-path row building (entries without recorded applied paths).
/// </summary>
public class AclManagerTraverseRowBuilder(
    IAclPathIconProvider iconProvider,
    TraverseEntryResolver resolver)
{
    private DataGridView _traverseGrid = null!;
    private SecurityIdentifier _sidIdentity = null!;
    private Font? _boldFont;
    private AclManagerPendingChanges _pending = null!;
    private IReadOnlyList<string> _groupSids = null!;

    public void Initialize(
        DataGridView traverseGrid,
        string sid,
        AclManagerPendingChanges pending,
        IReadOnlyList<string> groupSids)
    {
        _traverseGrid = traverseGrid;
        _sidIdentity = new SecurityIdentifier(sid);
        _pending = pending;
        _groupSids = groupSids;
    }

    public HashSet<GrantedPathEntry> FixableEntries { get; } = new();

    public void ClearFixableEntries() => FixableEntries.Clear();

    public void DisposeBoldFont()
    {
        _boldFont?.Dispose();
        _boldFont = null;
    }

    /// <summary>
    /// Adds row(s) for an entry that has recorded <see cref="GrantedPathEntry.AllAppliedPaths"/>
    /// (tracked traverse format). Shows the original entry grayed when the path is missing,
    /// plus a synthetic nearest-ancestor row when available.
    /// </summary>
    public void AddTrackedTraverseRow(GrantedPathEntry entry)
    {
        var groupSids = _groupSids;

        if (!AclHelper.PathExists(entry.Path))
        {
            AddSingleTraverseRow(entry, isGray: true);
            var (synthetic, allEffective) = resolver.CreateNearestAncestorEntry(entry, _sidIdentity, groupSids);
            if (synthetic != null)
            {
                if (synthetic.AllAppliedPaths is { Count: > 0 })
                    AddSingleTraverseRow(synthetic, isGray: false, isYellow: !allEffective);
                else
                    AddSingleTraverseRow(synthetic, isGray: false);
            }
        }
        else
        {
            // Yellow only if any path in AllAppliedPaths lacks effective traverse
            // (explicit + inherited + group membership) — not just an explicit ACE.
            bool allEffective = entry.AllAppliedPaths!.All(p => resolver.HasEffectiveTraverse(p, _sidIdentity, groupSids));
            AddSingleTraverseRow(entry, isGray: false, isYellow: !allEffective);
        }
    }

    /// <summary>
    /// Adds a row for an entry without recorded <see cref="GrantedPathEntry.AllAppliedPaths"/>
    /// (legacy traverse format). Walks the ancestor chain to populate applied paths and
    /// upgrades the entry to tracked format in-place.
    /// </summary>
    public void AddLegacyTraverseRow(GrantedPathEntry entry)
    {
        var groupSids = _groupSids;

        if (!AclHelper.PathExists(entry.Path))
        {
            AddSingleTraverseRow(entry, isGray: true);
        }
        else
        {
            // No AllAppliedPaths recorded (old entry) — walk ancestor chain for display.
            // Populates entry.AllAppliedPaths so bold/yellow checks cover the full path
            // chain rather than just entry.Path, and upgrades the entry to tracked format.
            var ancestorPaths = new List<string>();
            for (var dir = new DirectoryInfo(entry.Path); dir != null; dir = dir.Parent)
            {
                if (dir.Exists)
                    ancestorPaths.Add(dir.FullName);
            }

            if (ancestorPaths.Count > 0)
                entry.AllAppliedPaths = ancestorPaths;

            bool allEffective = ancestorPaths.Count > 0
                ? ancestorPaths.All(p => resolver.HasEffectiveTraverse(p, _sidIdentity, groupSids))
                : resolver.HasEffectiveTraverse(entry.Path, _sidIdentity, groupSids);
            AddSingleTraverseRow(entry, isGray: false, isYellow: !allEffective);
        }
    }

    private void AddSingleTraverseRow(GrantedPathEntry entry, bool isGray, bool isYellow = false)
    {
        var row = new DataGridViewRow();
        row.CreateCells(_traverseGrid);
        row.Tag = entry;

        row.Cells[_traverseGrid.Columns[AclManagerGrantsHelper.ColIcon]!.Index].Value =
            iconProvider.GetIcon(entry.Path);
        row.Cells[_traverseGrid.Columns["TraversePath"]!.Index].Value = entry.Path;

        var normalizedPath = Path.GetFullPath(entry.Path);
        bool isPendingGreen = _pending.Traverse.IsPendingTraverseAdd(normalizedPath) ||
                              _pending.Traverse.GetPendingFixesSnapshot().ContainsKey(normalizedPath) ||
                              _pending.Traverse.IsPendingTraverseConfigMove(normalizedPath);

        if (isPendingGreen)
        {
            // Green takes priority over yellow — intent is recorded, will be applied.
            AclManagerGrantRowRenderer.SetPendingRowColor(row);
        }
        else if (isGray)
        {
            row.DefaultCellStyle.ForeColor = Color.Gray;
            row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
        }
        else if (isYellow)
        {
            row.DefaultCellStyle.BackColor = Color.LightYellow;
            FixableEntries.Add(entry);
        }

        if (entry.AllAppliedPaths is { Count: > 0 })
            row.DefaultCellStyle.Font = _boldFont ??= new Font(_traverseGrid.Font, FontStyle.Bold);
        _traverseGrid.Rows.Add(row);
    }
}

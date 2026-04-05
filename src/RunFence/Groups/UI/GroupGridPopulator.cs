using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Groups.UI;

/// <summary>
/// Populates the groups grid and members grid from the local group membership service.
/// Also updates AppDatabase.SidNames with group names for unified display name resolution.
/// Call <see cref="Initialize"/> after construction to wire the grid controls.
/// </summary>
public class GroupGridPopulator(
    ILocalGroupMembershipService groupMembership,
    ILoggingService log,
    ISidNameCacheService sidNameCache)
{
    private DataGridView _groupsGrid = null!;
    private DataGridView _membersGrid = null!;
    private Label _membersHeaderLabel = null!;
    private string? _currentMemberGroupSid;

    public List<LocalUserAccount> LastGroups { get; private set; } = [];

    public void Initialize(DataGridView groupsGrid, DataGridView membersGrid, Label membersHeaderLabel)
    {
        _groupsGrid = groupsGrid;
        _membersGrid = membersGrid;
        _membersHeaderLabel = membersHeaderLabel;
    }

    public async Task PopulateGroups()
    {
        var selectedSid = GetSelectedGroupSid();

        var groupsTask = Task.Run(() =>
            GroupFilterHelper.FilterForGroupsPanel(groupMembership.GetLocalGroups()).ToList());
        var membersTask = selectedSid != null
            ? Task.Run(() => groupMembership.GetMembersOfGroup(selectedSid))
            : Task.FromResult<List<LocalUserAccount>>([]);

        List<LocalUserAccount> groups;
        List<LocalUserAccount> members;
        try
        {
            await Task.WhenAll(groupsTask, membersTask);
            groups = groupsTask.Result;
            members = membersTask.Result;
        }
        catch (Exception ex)
        {
            log.Error("Failed to populate groups grid", ex);
            return;
        }

        if (_groupsGrid.IsDisposed)
            return;

        // Re-read current selection after the async load — user may have changed it during the wait
        var targetSid = GetSelectedGroupSid() ?? selectedSid;

        LastGroups = groups;
        _groupsGrid.SuspendLayout();
        _groupsGrid.Rows.Clear();
        try
        {
            foreach (var group in LastGroups)
            {
                sidNameCache.ResolveAndCache(group.Sid, group.Username);

                var row = new DataGridViewRow();
                row.Tag = group.Sid;
                row.CreateCells(_groupsGrid, group.Username, group.Sid);
                _groupsGrid.Rows.Add(row);
            }

            // Restore selection — prefer current user selection, fall back to pre-load selection
            bool selectionRestored = false;
            if (targetSid != null)
            {
                foreach (DataGridViewRow row in _groupsGrid.Rows)
                {
                    if (string.Equals(row.Tag as string, targetSid, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Selected = true;
                        _groupsGrid.CurrentCell = row.Cells[0];
                        selectionRestored = true;
                        break;
                    }
                }
            }

            if (!selectionRestored && _groupsGrid.Rows.Count > 0)
            {
                _groupsGrid.Rows[0].Selected = true;
                _groupsGrid.CurrentCell = _groupsGrid.Rows[0].Cells[0];
            }
        }
        finally
        {
            _groupsGrid.ResumeLayout();
        }

        if (selectedSid != null && GetSelectedGroupSid() == selectedSid && !_membersGrid.IsDisposed)
            UpdateMembersGrid(members);
    }

    public async Task PopulateMembers(string groupSid)
    {
        var groupName = LastGroups.FirstOrDefault(g =>
            string.Equals(g.Sid, groupSid, StringComparison.OrdinalIgnoreCase))?.Username ?? groupSid;

        _membersHeaderLabel.Text = $"Members of {groupName}:";

        _currentMemberGroupSid = groupSid;
        _membersGrid.Rows.Clear();

        List<LocalUserAccount> members;
        try
        {
            members = await Task.Run(() => groupMembership.GetMembersOfGroup(groupSid));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to populate members for group SID {groupSid}", ex);
            return;
        }

        if (_membersGrid.IsDisposed || _currentMemberGroupSid != groupSid)
            return;

        UpdateMembersGrid(members);
    }

    private void UpdateMembersGrid(List<LocalUserAccount> members)
    {
        var selectedMemberSid = _membersGrid.SelectedRows.Count > 0 ? _membersGrid.SelectedRows[0].Tag as string : null;

        _membersGrid.SuspendLayout();
        _membersGrid.Rows.Clear();
        try
        {
            foreach (var member in members)
            {
                var row = new DataGridViewRow();
                row.Tag = member.Sid;
                row.CreateCells(_membersGrid, member.Username, member.Sid);
                _membersGrid.Rows.Add(row);
            }

            // Restore selected member
            if (selectedMemberSid != null)
            {
                foreach (DataGridViewRow row in _membersGrid.Rows)
                {
                    if (string.Equals(row.Tag as string, selectedMemberSid, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Selected = true;
                        _membersGrid.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }
        }
        finally
        {
            _membersGrid.ResumeLayout();
        }
    }

    public void ClearMembers()
    {
        _membersHeaderLabel.Text = "Members:";
        _membersGrid.Rows.Clear();
        _currentMemberGroupSid = null;
    }

    private string? GetSelectedGroupSid()
    {
        if (_groupsGrid.SelectedRows.Count == 0)
            return null;
        return _groupsGrid.SelectedRows[0].Tag as string;
    }
}

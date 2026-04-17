using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

/// <summary>
/// Manages the description text box editing behavior for a selected group:
/// tracking the current group SID, committing changes on leave/close, and
/// loading the description text after a group selection change.
/// </summary>
public class GroupDescriptionEditor(ILocalGroupMembershipService groupMembership, ILoggingService log)
{
    private TextBox _descriptionTextBox = null!;
    private string? _groupSid;
    private string? _originalDescription;

    public void Initialize(TextBox descriptionTextBox)
    {
        _descriptionTextBox = descriptionTextBox;
    }

    /// <summary>
    /// Begins loading the description for a new group: commits any pending edit on the
    /// previous group, then clears and disables the text box while the new description loads.
    /// </summary>
    public void BeginLoad(string? groupSid)
    {
        CommitDescription();
        _groupSid = groupSid;
        _originalDescription = null;
        _descriptionTextBox.Text = "";
        _descriptionTextBox.Enabled = false;
    }

    /// <summary>
    /// Returns true when the user is actively editing the description for the given group,
    /// meaning the text box is focused and the group SID matches the current one.
    /// Used to skip overwriting in-progress edits during a background refresh.
    /// </summary>
    public bool IsEditingGroup(string? groupSid)
        => string.Equals(groupSid, _groupSid, StringComparison.OrdinalIgnoreCase)
           && _descriptionTextBox.Focused;

    /// <summary>
    /// Applies the loaded description to the text box. Only updates UI when the supplied
    /// <paramref name="groupSid"/> still matches the current group, so stale async results
    /// are safely discarded.
    /// </summary>
    public void CompleteLoad(string? groupSid, string? desc, bool failed)
    {
        if (!string.Equals(groupSid, _groupSid, StringComparison.OrdinalIgnoreCase))
            return;

        if (failed)
        {
            _descriptionTextBox.Text = "";
            _descriptionTextBox.Enabled = false;
            _originalDescription = null;
        }
        else
        {
            var descText = desc ?? "";
            _originalDescription = descText.Trim();
            _descriptionTextBox.Text = descText;
            _descriptionTextBox.Enabled = true;
        }
    }

    /// <summary>
    /// Saves the current description if it differs from the original and the text box
    /// is not currently focused. Intended for use in Leave event handlers.
    /// </summary>
    public void SaveDescriptionIfChanged()
    {
        if (_descriptionTextBox.Focused)
            return;
        CommitDescription();
    }

    /// <summary>
    /// Saves the current description unconditionally if it differs from the original.
    /// Safe to call multiple times; no-op when nothing has changed or no group is selected.
    /// </summary>
    public void CommitDescription()
    {
        if (_groupSid == null || _originalDescription == null)
            return;
        var current = _descriptionTextBox.Text.Trim();
        if (current == _originalDescription)
            return;

        try
        {
            groupMembership.UpdateGroupDescription(_groupSid, current);
            _originalDescription = current;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save description for group {_groupSid}", ex);
        }
    }
}

using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

/// <summary>
/// Handles add/remove member operations for a selected group via dialogs.
/// </summary>
public class GroupMembershipOrchestrator(
    ILocalGroupMembershipService groupMembership,
    IMemberPickerDialog memberPicker,
    IGroupMembershipPrompt prompt,
    ILoggingService log)
{
    /// <summary>
    /// Opens the member picker dialog and adds selected users to the group.
    /// Returns true if any members were added.
    /// </summary>
    public bool AddMembers(string groupSid, string groupName, IReadOnlyList<string> existingMemberSids, IWin32Window? owner)
    {
        var existingSet = existingMemberSids.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedMembers = memberPicker.ShowPicker(groupName, existingSet, owner);
        if (selectedMembers == null || selectedMembers.Count == 0)
            return false;

        var errors = new List<string>();
        foreach (var member in selectedMembers)
        {
            try
            {
                groupMembership.AddUserToGroups(member.Sid, member.Username, [groupSid]);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to add {member.Username} to group {groupSid}", ex);
                errors.Add($"Failed to add '{member.Username}': {ex.Message}");
            }
        }

        if (errors.Count > 0)
            prompt.ShowErrors("Add Members", errors);

        return selectedMembers.Count > errors.Count;
    }

    /// <summary>
    /// Removes the specified member from the group after confirmation.
    /// Returns true if the member was removed.
    /// </summary>
    public bool RemoveMember(string groupSid, string memberSid, string memberName, IWin32Window? owner)
    {
        if (string.IsNullOrEmpty(memberSid))
            return false;

        if (!prompt.ConfirmRemove(memberName))
            return false;

        try
        {
            groupMembership.RemoveUserFromGroups(memberSid, memberName, [groupSid]);
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to remove member {memberSid} from group {groupSid}", ex);
            prompt.ShowErrors("Error", [$"Failed to remove member: {ex.Message}"]);
            return false;
        }
    }
}
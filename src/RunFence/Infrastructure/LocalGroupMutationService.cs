using System.DirectoryServices.AccountManagement;
using RunFence.Core;

namespace RunFence.Infrastructure;

public class LocalGroupMutationService(
    ILoggingService log,
    GroupMembershipApi groupMembership,
    ILocalGroupQueryMaintenanceService queryMaintenance) : ILocalGroupMutationService
{
    public GroupMutationResult AddUserToGroupsWithResult(string userSid, string username, List<string> groupSids)
    {
        try
        {
            AddUserToGroups(userSid, username, groupSids);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, null, null, groupSids, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, null, null, groupSids, ex.Message);
        }
    }

    public void AddUserToGroups(string userSid, string username, List<string> groupSids)
    {
        try
        {
            ModifyGroupMembership(userSid, username, groupSids, (group, user) => group.Members.Add(user), "add");
        }
        finally
        {
            queryMaintenance.InvalidateUserGroupMembership(userSid, groupSids);
        }
    }

    public GroupMutationResult RemoveUserFromGroupsWithResult(string userSid, string username, List<string> groupSids)
    {
        try
        {
            RemoveUserFromGroups(userSid, username, groupSids);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, null, null, groupSids, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, null, null, groupSids, ex.Message);
        }
    }

    public void RemoveUserFromGroups(string userSid, string username, List<string> groupSids)
    {
        try
        {
            ModifyGroupMembership(userSid, username, groupSids, (group, user) => group.Members.Remove(user), "remove");
        }
        finally
        {
            queryMaintenance.InvalidateUserGroupMembership(userSid, groupSids);
        }
    }

    public GroupMutationResult CreateGroupWithResult(string groupName, string? description)
    {
        try
        {
            var sid = CreateGroup(groupName, description);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, sid, groupName, null, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, null, groupName, null, ex.Message);
        }
    }

    public string CreateGroup(string groupName, string? description)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = new GroupPrincipal(context);
            group.Name = groupName;
            if (!string.IsNullOrEmpty(description))
                group.Description = description;
            group.Save();
            var sid = group.Sid.Value;
            log.Info($"Created local group: {groupName} ({sid})");
            return sid;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to create local group {groupName}", ex);
            throw new InvalidOperationException($"Failed to create group: {ex.Message}", ex);
        }
        finally
        {
            queryMaintenance.InvalidateLocalGroups();
        }
    }

    public GroupMutationResult DeleteGroupWithResult(string groupSid)
    {
        try
        {
            DeleteGroup(groupSid);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, groupSid, null, null, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, groupSid, null, null, ex.Message);
        }
    }

    public void DeleteGroup(string groupSid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
            if (group != null)
            {
                var name = group.SamAccountName;
                group.Delete();
                log.Info($"Deleted local group: {name} ({groupSid})");
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to delete group SID {groupSid}", ex);
            throw new InvalidOperationException($"Failed to delete group: {ex.Message}", ex);
        }
        finally
        {
            queryMaintenance.InvalidateLocalGroups();
            queryMaintenance.InvalidateGroupDetails(groupSid);
        }
    }

    public GroupMutationResult UpdateGroupDescriptionWithResult(string groupSid, string description)
    {
        try
        {
            UpdateGroupDescription(groupSid, description);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, groupSid, null, null, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, groupSid, null, null, ex.Message);
        }
    }

    public void UpdateGroupDescription(string groupSid, string description)
    {
        try
        {
            var groupName = queryMaintenance.ResolveGroupName(groupSid)
                ?? throw new InvalidOperationException($"Group not found for SID {groupSid}.");
            groupMembership.UpdateLocalGroupDescription(groupName, description, groupSid);
            log.Info($"Updated description for group {groupName} ({groupSid})");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to update group description for SID {groupSid}", ex);
            throw new InvalidOperationException($"Failed to update group: {ex.Message}", ex);
        }
        finally
        {
            queryMaintenance.InvalidateGroupDetails(groupSid);
        }
    }

    private void ModifyGroupMembership(
        string userSid,
        string username,
        IEnumerable<string> groupSids,
        Action<GroupPrincipal, UserPrincipal> operation,
        string operationVerb)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, userSid);
        if (user == null)
            throw new InvalidOperationException($"Account not found for SID {userSid}.");

        var pastVerb = operationVerb == "add" ? "Added" : "Removed";
        var preposition = operationVerb == "add" ? "to" : "from";
        var errors = new List<string>();
        foreach (var groupSid in groupSids)
        {
            try
            {
                using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
                if (group == null)
                {
                    log.Error($"Group not found for SID: {groupSid}");
                    errors.Add($"Group with SID '{groupSid}' not found");
                    continue;
                }

                var groupName = group.SamAccountName ?? groupSid;
                operation(group, user);
                group.Save();
                log.Info($"{pastVerb} {username} {preposition} group {groupName}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to {operationVerb} {username} {preposition} group SID {groupSid}", ex);
                errors.Add($"Failed to {operationVerb} {preposition} group '{groupSid}': {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));
    }
}

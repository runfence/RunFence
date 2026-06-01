using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class GrantMutationOrderResolver
{
    public GrantMutationOrder ForRightsChange(GrantedPathEntry? priorEntry, GrantedPathEntry newEntry)
    {
        if (priorEntry == null)
            return GrantMutationOrder.SaveThenApply;

        if (priorEntry.IsDeny != newEntry.IsDeny)
            return GrantMutationOrder.RemoveSaveAdd;

        var previousRights = priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(priorEntry.IsDeny);
        var nextRights = newEntry.SavedRights ?? SavedRightsState.DefaultForMode(newEntry.IsDeny);
        bool hasAdditions =
            (nextRights.Execute && !previousRights.Execute) ||
            (nextRights.Write && !previousRights.Write) ||
            (nextRights.Read && !previousRights.Read) ||
            (nextRights.Special && !previousRights.Special) ||
            (nextRights.Own && !previousRights.Own);
        bool hasRemovals =
            (previousRights.Execute && !nextRights.Execute) ||
            (previousRights.Write && !nextRights.Write) ||
            (previousRights.Read && !nextRights.Read) ||
            (previousRights.Special && !nextRights.Special) ||
            (previousRights.Own && !nextRights.Own);

        return ForAclDelta(hasAdditions, hasRemovals);
    }

    public GrantMutationOrder ForAclDelta(bool hasAclAdditions, bool hasAclRemovals)
    {
        if (hasAclAdditions && hasAclRemovals)
            return GrantMutationOrder.RemoveSaveAdd;

        if (hasAclRemovals)
            return GrantMutationOrder.ApplyThenSave;

        return GrantMutationOrder.SaveThenApply;
    }
}

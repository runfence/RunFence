using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public class AppEntryChangeClassifier
{
    public AppEntryChangeSet Classify(
        AppEntry previousApp,
        AppEntry newApp,
        IReadOnlyList<HandlerAssociationItem> previousAssociations,
        IReadOnlyList<HandlerAssociationItem> newAssociations,
        string? previousConfigPath,
        string? newConfigPath)
    {
        var pathChanged = !string.Equals(previousApp.ExePath, newApp.ExePath, StringComparison.OrdinalIgnoreCase);
        var pathTypeChanged = previousApp.IsFolder != newApp.IsFolder || previousApp.IsUrlScheme != newApp.IsUrlScheme;
        var accountIdentityChanged =
            !string.Equals(previousApp.AccountSid, newApp.AccountSid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previousApp.AppContainerName, newApp.AppContainerName, StringComparison.OrdinalIgnoreCase);
        var appNameChanged = !string.Equals(previousApp.Name, newApp.Name, StringComparison.Ordinal);
        var manageShortcutsChanged = previousApp.ManageShortcuts != newApp.ManageShortcuts;
        var configPathChanged = !string.Equals(previousConfigPath, newConfigPath, StringComparison.OrdinalIgnoreCase);

        var requiresAclReapply =
            pathChanged ||
            pathTypeChanged ||
            accountIdentityChanged ||
            previousApp.RestrictAcl != newApp.RestrictAcl ||
            previousApp.AclMode != newApp.AclMode ||
            previousApp.DeniedRights != newApp.DeniedRights ||
            previousApp.AclTarget != newApp.AclTarget ||
            previousApp.FolderAclDepth != newApp.FolderAclDepth ||
            !AllowAclEntriesEqual(previousApp.AllowedAclEntries, newApp.AllowedAclEntries);

        var requiresManagedShortcutRefresh =
            appNameChanged ||
            !string.Equals(previousApp.Id, newApp.Id, StringComparison.Ordinal) ||
            accountIdentityChanged ||
            manageShortcutsChanged ||
            !string.Equals(previousApp.DefaultArguments, newApp.DefaultArguments, StringComparison.Ordinal) ||
            previousApp.AllowPassingArguments != newApp.AllowPassingArguments ||
            !string.Equals(previousApp.WorkingDirectory, newApp.WorkingDirectory, StringComparison.Ordinal) ||
            previousApp.AllowPassingWorkingDirectory != newApp.AllowPassingWorkingDirectory ||
            pathTypeChanged;

        var requiresIconRefresh =
            appNameChanged ||
            accountIdentityChanged ||
            pathChanged ||
            pathTypeChanged ||
            (manageShortcutsChanged && (previousApp.IsUrlScheme || newApp.IsUrlScheme));

        return new AppEntryChangeSet(
            RequiresAclReapply: requiresAclReapply,
            RequiresBesideTargetRefresh: pathChanged || pathTypeChanged || appNameChanged || accountIdentityChanged,
            RequiresHandlerSync: !AssociationsEqual(previousAssociations, newAssociations),
            RequiresManagedShortcutRefresh: requiresManagedShortcutRefresh,
            RequiresIconRefresh: requiresIconRefresh,
            ConfigSaveScope: configPathChanged
                ? AppEditConfigSaveScope.AllConfigs
                : AppEditConfigSaveScope.CurrentAppConfigOnly);
    }

    private static bool AssociationsEqual(
        IReadOnlyList<HandlerAssociationItem> left,
        IReadOnlyList<HandlerAssociationItem> right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left.Count != right.Count)
            return false;

        var orderedLeft = left.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var orderedRight = right.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < orderedLeft.Count; i++)
        {
            if (!AssociationEqual(orderedLeft[i], orderedRight[i]))
                return false;
        }

        return true;
    }

    private static bool AssociationEqual(HandlerAssociationItem left, HandlerAssociationItem right)
    {
        if (!string.Equals(left.Key, right.Key, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.ArgumentsTemplate, right.ArgumentsTemplate, StringComparison.Ordinal) ||
            left.ReplacePrefixes != right.ReplacePrefixes)
        {
            return false;
        }

        return StringListsEqual(left.PathPrefixes, right.PathPrefixes);
    }

    private static bool AllowAclEntriesEqual(List<AllowAclEntry>? left, List<AllowAclEntry>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left == null || right == null)
            return left == null && right == null;

        if (left.Count != right.Count)
            return false;

        var orderedLeft = left
            .OrderBy(item => item.Sid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AllowExecute)
            .ThenBy(item => item.AllowWrite)
            .ToList();
        var orderedRight = right
            .OrderBy(item => item.Sid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AllowExecute)
            .ThenBy(item => item.AllowWrite)
            .ToList();

        for (int i = 0; i < orderedLeft.Count; i++)
        {
            if (!string.Equals(orderedLeft[i].Sid, orderedRight[i].Sid, StringComparison.OrdinalIgnoreCase) ||
                orderedLeft[i].AllowExecute != orderedRight[i].AllowExecute ||
                orderedLeft[i].AllowWrite != orderedRight[i].AllowWrite)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringListsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left == null || right == null)
            return left == null && right == null;

        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}

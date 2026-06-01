using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;
using RunFence.Acl;

namespace RunFence.Apps;

public class AppEntryEnforcementCoordinator(
    IAclService aclService,
    AppEntryAclEnforcer aclEnforcer,
    AppEntryNonAclEnforcer nonAclEnforcer)
{
    public enum EnforcementFailureKind
    {
        None,
        Cleanup,
        Required,
        Convenience
    }

    public sealed record EnforcementResult(
        EnforcementFailureKind FailureKind,
        Exception? Exception = null)
    {
        public bool Succeeded => FailureKind == EnforcementFailureKind.None;
        public string? Message => Exception?.Message;
    }

    public sealed record WizardAppFailure(
        AppEntry App,
        Exception Exception);

    public sealed record WizardEnforcementResult(
        IReadOnlyList<WizardAppFailure> AppFailures,
        Exception? AncestorAclRecomputeFailure);

    public static bool RequiresEnforcement(AppEntryChangeSet changeSet)
        => changeSet.RequiresAclReapply ||
           changeSet.RequiresBesideTargetRefresh ||
           changeSet.RequiresManagedShortcutRefresh ||
           changeSet.RequiresIconRefresh;

    public void ApplyChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache)
    {
        aclEnforcer.Apply(app, allApps);
        nonAclEnforcer.ApplyAll(app, shortcutCache);
    }

    public void RevertChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache)
    {
        aclEnforcer.Revert(app, allApps);
        nonAclEnforcer.RevertAll(app, shortcutCache);
    }

    public void ApplyTargetedChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        if (changeSet.RequiresAclReapply)
            aclEnforcer.Apply(app, allApps);

        nonAclEnforcer.ApplyTargeted(app, shortcutCache, changeSet);
    }

    public void ApplyTargetedChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet,
        string? iconPathOverrideForBesideTargetShortcut)
    {
        if (changeSet.RequiresAclReapply)
            aclEnforcer.Apply(app, allApps);

        nonAclEnforcer.ApplyTargeted(
            app,
            shortcutCache,
            changeSet,
            iconPathOverrideForBesideTargetShortcut);
    }

    public void RevertTargetedChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        if (changeSet.RequiresAclReapply)
            aclEnforcer.Revert(app, allApps);

        nonAclEnforcer.RevertTargeted(app, shortcutCache, changeSet);
    }

    public EnforcementResult ApplyRunAsChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        if (!RequiresEnforcement(changeSet))
            return new EnforcementResult(EnforcementFailureKind.None);

        if (changeSet.RequiresAclReapply)
        {
            try
            {
                aclEnforcer.Apply(app, allApps);
            }
            catch (Exception ex)
            {
                return new EnforcementResult(
                    app.AclMode == AclMode.Deny
                        ? EnforcementFailureKind.Convenience
                        : EnforcementFailureKind.Required,
                    ex);
            }
        }

        try
        {
            nonAclEnforcer.ApplyTargeted(app, shortcutCache, changeSet);
        }
        catch (Exception ex)
        {
            return new EnforcementResult(
                EnforcementFailureKind.Convenience,
                ex);
        }

        try
        {
            if (changeSet.RequiresAclReapply)
                aclService.RecomputeAllAncestorAcls(allApps);
        }
        catch (Exception ex)
        {
            return new EnforcementResult(
                EnforcementFailureKind.Required,
                ex);
        }

        return new EnforcementResult(EnforcementFailureKind.None);
    }

    public EnforcementResult RevertRunAsChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        if (!RequiresEnforcement(changeSet))
            return new EnforcementResult(EnforcementFailureKind.None);

        try
        {
            if (changeSet.RequiresAclReapply)
                aclEnforcer.Revert(app, allApps);

            nonAclEnforcer.RevertTargeted(app, shortcutCache, changeSet);

            if (changeSet.RequiresAclReapply)
            {
                var appsAfterRevert = allApps.Where(existingApp => existingApp.Id != app.Id).ToList();
                aclService.RecomputeAllAncestorAcls(appsAfterRevert);
            }
        }
        catch (Exception ex)
        {
            return new EnforcementResult(
                EnforcementFailureKind.Cleanup,
                ex);
        }

        return new EnforcementResult(EnforcementFailureKind.None);
    }

    public WizardEnforcementResult ApplyWizardChanges(
        IReadOnlyList<AppEntry> createdApps,
        IReadOnlyList<AppEntry> allApps,
        Func<AppEntry, ShortcutTraversalCache> shortcutCacheFactory,
        bool createDesktopShortcut)
    {
        List<WizardAppFailure> appFailures = [];
        foreach (var app in createdApps)
        {
            Exception? appFailure = null;
            try
            {
                ApplyChanges(app, allApps, shortcutCacheFactory(app));
            }
            catch (Exception ex)
            {
                appFailure = ex;
            }

            if (createDesktopShortcut)
            {
                try
                {
                    nonAclEnforcer.CreateDesktopShortcut(app);
                }
                catch (Exception ex)
                {
                    appFailure ??= ex;
                }
            }

            if (appFailure != null)
            {
                appFailures.Add(new WizardAppFailure(app, appFailure));
            }
        }

        if (createdApps.Count == 0)
            return new WizardEnforcementResult(appFailures, null);

        try
        {
            aclService.RecomputeAllAncestorAcls(allApps);
            return new WizardEnforcementResult(appFailures, null);
        }
        catch (Exception ex)
        {
            return new WizardEnforcementResult(appFailures, ex);
        }
    }
}

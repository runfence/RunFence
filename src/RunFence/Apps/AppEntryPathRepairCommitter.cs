using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Apps;

public sealed class AppEntryPathRepairCommitter(
    ISessionProvider sessionProvider,
    IAppConfigService appConfigService,
    IIconService iconService,
    AppEntryEnforcementCoordinator enforcementCoordinator,
    IAclService aclService)
{
    private static readonly AppEntryChangeSet PathRepairChangeSet = new(
        RequiresAclReapply: true,
        RequiresBesideTargetRefresh: true,
        RequiresHandlerSync: false,
        RequiresManagedShortcutRefresh: false,
        RequiresIconRefresh: true,
        ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly);

    private static readonly AppEntryChangeSet SaveFailureRestoreChangeSet = PathRepairChangeSet with
    {
        RequiresIconRefresh = false
    };

    public AppEntryPathRepairResult Commit(AppEntry app, VersionedPathRepairResult repair)
    {
        var session = sessionProvider.GetSession();
        var previousApp = app.Clone();
        var appsBeforeRepair = session.Database.Apps
            .Select(candidate => ReferenceEquals(candidate, app) || string.Equals(candidate.Id, app.Id, StringComparison.Ordinal)
                ? previousApp
                : candidate)
            .ToList();

        try
        {
            enforcementCoordinator.RevertTargetedChanges(previousApp, appsBeforeRepair, new ShortcutTraversalCache([]), PathRepairChangeSet);
            aclService.RecomputeAllAncestorAcls(appsBeforeRepair);

            app.ExePath = repair.RepairedPath;
            app.LastKnownExeTimestamp = GetRepairedTimestamp(app);

            appConfigService.SaveConfigForApp(
                app.Id,
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        }
        catch (Exception ex)
        {
            RestorePreviousAppState(app, previousApp);
            var warning = RestorePreviousEnforcement(previousApp, session.Database.Apps, ex.Message);
            return new AppEntryPathRepairResult(app, Repaired: false, SaveFailed: true, WarningMessage: warning);
        }

        string? enforcementWarning = null;
        try
        {
            enforcementCoordinator.ApplyTargetedChanges(app, session.Database.Apps, new ShortcutTraversalCache([]), PathRepairChangeSet);
            aclService.RecomputeAllAncestorAcls(session.Database.Apps);
        }
        catch (Exception ex)
        {
            enforcementWarning = ex.Message;
        }

        return new AppEntryPathRepairResult(app, Repaired: true, SaveFailed: false, WarningMessage: enforcementWarning);
    }

    private string RestorePreviousEnforcement(AppEntry previousApp, IReadOnlyList<AppEntry> allApps, string saveFailureMessage)
    {
        var restoreWarnings = new List<string>();
        var previousIconPath = iconService.GetIconPath(previousApp.Id);

        try
        {
            enforcementCoordinator.ApplyTargetedChanges(
                previousApp,
                allApps,
                new ShortcutTraversalCache([]),
                SaveFailureRestoreChangeSet,
                previousIconPath);
        }
        catch (Exception ex)
        {
            restoreWarnings.Add($"Enforcement restore failed: {ex.Message}");
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(allApps);
        }
        catch (Exception ex)
        {
            restoreWarnings.Add($"Ancestor ACL recompute failed: {ex.Message}");
        }

        return restoreWarnings.Count == 0
            ? saveFailureMessage
            : $"{saveFailureMessage}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, restoreWarnings)}";
    }

    private static void RestorePreviousAppState(AppEntry app, AppEntry previousApp)
    {
        app.ExePath = previousApp.ExePath;
        app.LastKnownExeTimestamp = previousApp.LastKnownExeTimestamp;
    }

    private static DateTime? GetRepairedTimestamp(AppEntry app)
    {
        if (app.IsFolder)
            return null;

        return File.Exists(app.ExePath)
            ? File.GetLastWriteTimeUtc(app.ExePath)
            : null;
    }
}

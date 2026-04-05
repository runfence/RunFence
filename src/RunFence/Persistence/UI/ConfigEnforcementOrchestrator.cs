using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Persistence.UI;

/// <summary>Result of <see cref="ConfigEnforcementOrchestrator.CleanupAllApps"/>.</summary>
public enum CleanupAllAppsResult
{
    /// <summary>Cleanup completed and the caller should exit the application.</summary>
    ReadyToExit,

    /// <summary>Cleanup was skipped because an operation was already in progress.</summary>
    OperationInProgress
}

/// <summary>
/// Handles enforcement operations for loaded/unloaded app configs:
/// applies ACLs and shortcuts when loading, reverts them when unloading,
/// and performs full cleanup on shutdown.
/// </summary>
public class ConfigEnforcementOrchestrator
{
    private readonly SessionContext _session;
    private readonly IAclService _aclService;
    private readonly IIconService _iconService;
    private readonly AppEntryEnforcementHelper _enforcementHelper;
    private readonly IContextMenuService _contextMenuService;
    private readonly IAppHandlerRegistrationService _handlerRegistrationService;
    private readonly IFolderHandlerService _folderHandlerService;
    private readonly ILoggingService _log;

    public ConfigEnforcementOrchestrator(
        SessionContext session,
        IAclService aclService,
        IIconService iconService,
        IContextMenuService contextMenuService,
        ILoggingService log,
        AppEntryEnforcementHelper enforcementHelper,
        IAppHandlerRegistrationService handlerRegistrationService,
        IFolderHandlerService folderHandlerService)
    {
        _session = session;
        _aclService = aclService;
        _iconService = iconService;
        _contextMenuService = contextMenuService;
        _handlerRegistrationService = handlerRegistrationService;
        _folderHandlerService = folderHandlerService;
        _log = log;
        _enforcementHelper = enforcementHelper;
    }

    public void ApplyLoadedAppsEnforcement(IReadOnlyList<AppEntry> loadedApps)
    {
        foreach (var app in loadedApps)
        {
            try
            {
                _enforcementHelper.ApplyChanges(app, _session.Database.Apps, _session.Database.SidNames);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to apply changes for '{app.Name}' during LoadApps", ex);
            }
        }

        _aclService.RecomputeAllAncestorAcls(_session.Database.Apps);
    }

    public void RevertApps(IEnumerable<AppEntry> apps)
    {
        var remainingApps = _session.Database.Apps;
        foreach (var app in apps)
        {
            try
            {
                _enforcementHelper.RevertChanges(app, remainingApps.Append(app).ToList());
                _iconService.DeleteIcon(app.Id);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to revert app '{app.Name}'", ex);
            }
        }
    }

    public void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps)
        => _aclService.RecomputeAllAncestorAcls(allApps);

    public CleanupAllAppsResult CleanupAllApps(bool isEnforcementInProgress, bool isOperationInProgress)
    {
        if (isEnforcementInProgress || isOperationInProgress)
            return CleanupAllAppsResult.OperationInProgress;

        try
        {
            var database = _session.Database;
            foreach (var app in database.Apps.ToList())
            {
                try
                {
                    _enforcementHelper.RevertChanges(app, database.Apps);
                }
                catch (Exception ex)
                {
                    _log.Error($"Cleanup failed for {app.Name}", ex);
                }
            }

            _aclService.RecomputeAllAncestorAcls([]);
            _log.Info("Cleanup complete, exiting");
        }
        catch (Exception ex)
        {
            _log.Error("Cleanup failed", ex);
        }

        _contextMenuService.Unregister();
        _handlerRegistrationService.UnregisterAll();
        _folderHandlerService.UnregisterAll();
        return CleanupAllAppsResult.ReadyToExit;
    }
}
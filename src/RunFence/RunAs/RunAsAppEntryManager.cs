using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.RunAs;

/// <summary>
/// Manages app entry persistence, enforcement, and shortcut creation for the RunAs flow.
/// Dialog display is handled by <see cref="RunAsAppEditDialogHandler"/>.
/// </summary>
public class RunAsAppEntryManager
{
    private readonly IAppStateProvider _appState;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly IDataChangeNotifier _dataChangeNotifier;
    private readonly ILoggingService _log;
    private readonly SessionContext _session;
    private readonly IAppConfigService _appConfigService;
    private readonly IAclService _aclService;
    private readonly IShortcutService _shortcutService;
    private readonly IIconService _iconService;
    private readonly AppEntryEnforcementHelper _enforcementHelper;
    private readonly ISidResolver _sidResolver;
    private readonly ILicenseService _licenseService;
    private readonly IRunAsLaunchErrorHandler _launchErrorHandler;

    public RunAsAppEntryManager(
        IAppStateProvider appState,
        IUiThreadInvoker uiThreadInvoker,
        IDataChangeNotifier dataChangeNotifier,
        ILoggingService log,
        SessionContext session,
        IAppConfigService appConfigService,
        IAclService aclService,
        IShortcutService shortcutService,
        IIconService iconService,
        AppEntryEnforcementHelper enforcementHelper,
        ISidResolver sidResolver,
        ILicenseService licenseService,
        IRunAsLaunchErrorHandler launchErrorHandler)
    {
        _appState = appState;
        _uiThreadInvoker = uiThreadInvoker;
        _dataChangeNotifier = dataChangeNotifier;
        _log = log;
        _session = session;
        _appConfigService = appConfigService;
        _aclService = aclService;
        _shortcutService = shortcutService;
        _iconService = iconService;
        _enforcementHelper = enforcementHelper;
        _sidResolver = sidResolver;
        _licenseService = licenseService;
        _launchErrorHandler = launchErrorHandler;
    }

    /// <summary>
    /// Persists a new AppEntry: adds to database, saves config, applies ACL/shortcuts,
    /// and notifies the UI. Returns true on success; removes the app from the database on failure.
    /// </summary>
    public bool PersistNewAppEntry(AppEntry app, string? configPath)
    {
        if (!_licenseService.CanAddApp(_appState.Database.Apps.Count))
        {
            _uiThreadInvoker.BeginInvoke(() =>
                MessageBox.Show(_licenseService.GetRestrictionMessage(EvaluationFeature.Apps, _appState.Database.Apps.Count),
                    "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information));
            return false;
        }

        try
        {
            _appState.Database.Apps.Add(app);

            if (configPath != null)
                _appConfigService.AssignApp(app.Id, configPath);

            using var scope = _session.PinDerivedKey.Unprotect();
            _appConfigService.SaveConfigForApp(app.Id, _appState.Database,
                scope.Data, _session.CredentialStore.ArgonSalt);

            if (app.RestrictAcl)
            {
                try
                {
                    _aclService.ApplyAcl(app, _appState.Database.Apps);
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to apply ACL for RunAs app", ex);
                }
            }

            if (app.ManageShortcuts)
                CreateBesideTargetShortcut(app);

            _aclService.RecomputeAllAncestorAcls(_appState.Database.Apps);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to create RunAs app entry", ex);
            // Revert ACLs/shortcuts before removing app (app must still be in allApps for RevertAcl)
            if (app.RestrictAcl)
                try
                {
                    _aclService.RevertAcl(app, _appState.Database.Apps);
                }
                catch
                {
                }

            if (app.ManageShortcuts)
                try
                {
                    _shortcutService.RemoveBesideTargetShortcut(app);
                }
                catch
                {
                }

            _appState.Database.Apps.Remove(app);
            try
            {
                _aclService.RecomputeAllAncestorAcls(_appState.Database.Apps);
            }
            catch
            {
            }

            if (configPath != null)
                _appConfigService.RemoveApp(app.Id);
            return false;
        }

        try
        {
            _dataChangeNotifier.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to refresh UI after RunAs app creation: {ex.Message}");
        }

        return true;
    }

    private void CreateBesideTargetShortcut(AppEntry app)
    {
        try
        {
            var iconPath = _iconService.CreateBadgedIcon(app);
            var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
            if (!File.Exists(launcherPath))
                return;

            string? effectiveSid;
            if (app.AppContainerName != null)
            {
                effectiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
            }
            else
            {
                var credential = _session.CredentialStore.Credentials
                    .FirstOrDefault(c => string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));
                if (credential == null)
                    return;
                effectiveSid = credential.Sid;
            }

            if (string.IsNullOrEmpty(effectiveSid))
                return;
            var username = SidNameResolver.ResolveUsername(effectiveSid, _sidResolver, _appState.Database.SidNames);
            if (!string.IsNullOrEmpty(username))
                _shortcutService.CreateBesideTargetShortcut(app, launcherPath, iconPath, username);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to create shortcuts for RunAs app", ex);
        }
    }

    public void RevertAppChanges(AppEntry app)
    {
        try
        {
            _enforcementHelper.RevertChanges(app, _appState.Database.Apps);
            var appsAfterRevert = _appState.Database.Apps.Where(a => a.Id != app.Id).ToList();
            _aclService.RecomputeAllAncestorAcls(appsAfterRevert);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to revert changes for {app.Name}", ex);
        }
    }

    public void ApplyAppChanges(AppEntry app)
    {
        _enforcementHelper.ApplyChanges(app, _appState.Database.Apps, _appState.Database.SidNames);
        _aclService.RecomputeAllAncestorAcls(_appState.Database.Apps);
    }

    public void TryUpdateOriginalShortcut(string originalLnkPath, string appId)
    {
        try
        {
            var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
            var iconPath = Path.Combine(Constants.ProgramDataIconDir, $"{appId}.ico");
            _shortcutService.UpdateShortcutToLauncher(
                originalLnkPath, appId, launcherPath,
                File.Exists(iconPath) ? iconPath : null);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to update shortcut to launcher", ex);
        }
    }

    public void RunWithLaunchErrorHandling(Action launchAction, string filePath)
        => _launchErrorHandler.RunWithErrorHandling(launchAction, filePath);
}
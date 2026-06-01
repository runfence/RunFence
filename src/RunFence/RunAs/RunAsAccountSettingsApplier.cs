using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.PrefTrans;

namespace RunFence.RunAs;

/// <summary>
/// Applies post-creation account settings: privilege level, firewall DB settings,
/// desktop settings import, and firewall rule enforcement. Extracted from RunAsUserAccountCreator to
/// reduce its dependency count.
/// </summary>
public class RunAsAccountSettingsApplier(
    IAppStateProvider appState,
    SessionContext session,
    IMainConfigPersistence mainConfigPersistence,
    ILoggingService log,
    ISettingsTransferService settingsTransferService,
    FirewallApplyHelper firewallApplyHelper,
    IAccountCreationProgressRunner progressRunner)
{
    /// <summary>
    /// Applies the privilege level default for a newly created account.
    /// </summary>
    public void ApplyLaunchDefaults(string sid, PrivilegeLevel privilegeLevel)
    {
        var entry = appState.Database.GetOrCreateAccount(sid);
        entry.PrivilegeLevel = privilegeLevel;
    }

    /// <summary>
    /// Applies firewall DB settings for a newly created account.
    /// </summary>
    public void ApplyFirewallDbSettings(string sid, bool allowInternet, bool allowLocalhost, bool allowLan)
    {
        var fwSettings = new FirewallAccountSettings
        {
            AllowInternet = allowInternet,
            AllowLocalhost = allowLocalhost,
            AllowLan = allowLan
        };
        FirewallAccountSettings.UpdateOrRemove(appState.Database, sid, fwSettings);
    }

    /// <summary>
    /// Runs post-creation tasks (settings import, config save, firewall rule enforcement) with
    /// a progress dialog when needed. Collects errors into the provided list.
    /// </summary>
    public async Task RunPostCreationTasksAsync(
        string sid,
        string username,
        string? settingsImportPath,
        bool firewallSettingsChanged,
        List<string> errors)
    {
        bool needsProgress = settingsImportPath != null || firewallSettingsChanged;
        if (needsProgress)
        {
            await progressRunner.RunAsync(async progress =>
            {
                try
                {
                    if (settingsImportPath != null)
                    {
                        progress.SetStatus($"Importing desktop settings for {username}...");
                        try
                        {
                            var importResult = await SettingsImportHelper.ImportAsync(
                                settingsImportPath, sid,
                                settingsTransferService);
                            if (importResult.Status != SettingsImportStatus.Succeeded)
                                errors.Add($"Settings import: {string.Join("; ", importResult.Errors)}");
                        }
                        catch (Exception ex)
                        {
                            log.Error($"Settings import failed for {username}", ex);
                            errors.Add($"Settings import: {ex.Message}");
                        }
                    }

                    SaveConfig();

                    if (firewallSettingsChanged)
                    {
                        progress.SetStatus($"Applying firewall rules for {username}...");
                        var displayName = appState.Database.SidNames.GetValueOrDefault(sid)
                                          ?? username;
                        var fwSettings = appState.Database.GetAccount(sid)?.Firewall
                                         ?? new FirewallAccountSettings();
                        await firewallApplyHelper.ApplyWithRollbackAsync(
                            sid: sid,
                            username: displayName,
                            previous: null,
                            final: fwSettings,
                            database: appState.Database,
                            saveAction: SaveConfig,
                            reportError: errors.Add);
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Account post-creation setup failed for {username}", ex);
                    errors.Add($"Account setup: {ex.Message}");
                }
            });
        }
        else
        {
            SaveConfig();
        }
    }

    private void SaveConfig()
    {
        mainConfigPersistence.SaveConfig(
            appState.Database,
            session.PinDerivedKey,
            session.CredentialStore.ArgonSalt);
    }
}

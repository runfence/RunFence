using RunFence.Account.UI.Forms;
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
    IDatabaseService databaseService,
    ILoggingService log,
    ISettingsTransferService settingsTransferService,
    FirewallApplyHelper firewallApplyHelper)
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
    public void RunPostCreationTasks(
        string sid,
        string username,
        string? settingsImportPath,
        bool firewallSettingsChanged,
        List<string> errors)
    {
        bool needsProgress = settingsImportPath != null || firewallSettingsChanged;
        if (needsProgress)
        {
            using var progressForm = new AccountCreationProgressForm();
            progressForm.Shown += async (_, _) =>
            {
                try
                {
                    if (settingsImportPath != null)
                    {
                        progressForm.SetStatus($"Importing desktop settings for {username}...");
                        try
                        {
                            var (error, _) = await SettingsImportHelper.ImportAsync(
                                settingsImportPath, sid,
                                settingsTransferService);
                            if (error != null)
                                errors.Add($"Settings import: {error}");
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
                        progressForm.SetStatus($"Applying firewall rules for {username}...");
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
                finally
                {
                    progressForm.DialogResult = DialogResult.OK;
                }
            };
            progressForm.ShowDialog();
        }
        else
        {
            SaveConfig();
        }
    }

    private void SaveConfig()
    {
        using var scope = session.PinDerivedKey.Unprotect();
        databaseService.SaveConfig(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
    }
}

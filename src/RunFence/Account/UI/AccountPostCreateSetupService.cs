using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.PrefTrans;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the post-account-creation setup flow: settings import, firewall rules application,
/// and package installation via progress form or inline.
/// </summary>
public class AccountPostCreateSetupService(
    ISettingsTransferService settingsTransferService,
    FirewallApplyHelper firewallApplyHelper,
    PackageInstallService packageInstallService,
    ISessionProvider sessionProvider,
    ILoggingService log)
{
    /// <summary>
    /// Runs the post-create setup flow. If settings import, firewall changes, or internet-blocked
    /// package installs are required, shows an <see cref="Forms.AccountCreationProgressForm"/>.
    /// The <paramref name="saveAndRefresh"/> callback must trigger a save and grid refresh.
    /// </summary>
    public async Task RunPostCreateSetupAsync(PostCreateSetupRequest request, Action saveAndRefresh)
    {
        bool hasPackages = request.SelectedInstallPackages.Count > 0;
        bool internetBlocked = request is { FirewallSettingsChanged: true, AllowInternet: false };

        bool needsProgress = request.SettingsImportPath != null
                             || request.FirewallSettingsChanged
                             || (hasPackages && internetBlocked);

        if (needsProgress)
        {
            using var progressForm = new AccountCreationProgressForm();
            progressForm.Shown += async (_, _) =>
            {
                try
                {
                    if (request.SettingsImportPath != null)
                    {
                        progressForm.SetStatus($"Importing desktop settings for {request.NewUsername}...");
                        try
                        {
                            var (error, _) = await SettingsImportHelper.ImportAsync(
                                request.SettingsImportPath, request.CreatedSid,
                                settingsTransferService);
                            if (error != null)
                                request.Errors.Add($"Settings import: {error}");
                        }
                        catch (Exception ex)
                        {
                            request.Errors.Add($"Settings import: {ex.Message}");
                        }
                    }

                    saveAndRefresh();

                    if (hasPackages && internetBlocked)
                    {
                        var refreshedSession = sessionProvider.GetSession();
                        var credEntry = refreshedSession.CredentialStore.Credentials.FirstOrDefault(c =>
                            string.Equals(c.Sid, request.CreatedSid, StringComparison.OrdinalIgnoreCase));
                        if (credEntry != null)
                        {
                            progressForm.SetStatus($"Installing packages for {request.NewUsername}...");
                            try
                            {
                                packageInstallService.InstallPackages(request.SelectedInstallPackages, new AccountLaunchIdentity(request.CreatedSid));
                                progressForm.SetStatus($"Waiting for install scripts to complete for {request.NewUsername}...");
                                await packageInstallService.WaitForInstallCompletionAsync(request.CreatedSid, TimeSpan.FromMinutes(10));
                            }
                            catch (Exception ex)
                            {
                                request.Errors.Add($"Install scripts: {ex.Message}");
                            }
                        }
                    }

                    if (request.FirewallSettingsChanged)
                    {
                        progressForm.SetStatus($"Applying firewall rules for {request.NewUsername}...");
                        var refreshedSession = sessionProvider.GetSession();
                        var username = refreshedSession.Database.SidNames.GetValueOrDefault(request.CreatedSid) ?? request.NewUsername;
                        var fwSettings = refreshedSession.Database.GetAccount(request.CreatedSid)?.Firewall ?? new FirewallAccountSettings();
                        var sid = request.CreatedSid;
                        await firewallApplyHelper.ApplyWithRollbackAsync(
                            sid: sid,
                            username: username,
                            previous: null,
                            final: fwSettings,
                            database: refreshedSession.Database,
                            saveAction: saveAndRefresh,
                            reportError: msg => request.Errors.Add(msg));
                    }
                }
                catch (Exception ex)
                {
                    request.Errors.Add($"Account setup: {ex.Message}");
                    log.Error("Unexpected error during post-create setup", ex);
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
            saveAndRefresh();
        }
    }
}

public class PostCreateSetupRequest(
    string? SettingsImportPath,
    string CreatedSid,
    string NewUsername,
    bool FirewallSettingsChanged,
    List<InstallablePackage> SelectedInstallPackages,
    bool AllowInternet,
    List<string> Errors)
{
    public string? SettingsImportPath { get; } = SettingsImportPath;
    public string CreatedSid { get; } = CreatedSid;
    public string NewUsername { get; } = NewUsername;
    public bool FirewallSettingsChanged { get; } = FirewallSettingsChanged;
    public List<InstallablePackage> SelectedInstallPackages { get; } = SelectedInstallPackages;
    public bool AllowInternet { get; } = AllowInternet;
    public List<string> Errors { get; } = Errors;
}

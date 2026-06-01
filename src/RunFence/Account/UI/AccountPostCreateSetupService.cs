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
    IPackageInstallService packageInstallService,
    ISessionProvider sessionProvider,
    IAccountCreationProgressRunner progressRunner,
    ILoggingService log)
{
    /// <summary>
    /// Runs the post-create setup flow. If settings import, firewall changes, or internet-blocked
    /// package installs are required, shows a cancellable progress form.
    /// The <paramref name="saveAndRefresh"/> callback must trigger a save and grid refresh.
    /// </summary>
    public async Task RunPostCreateSetupAsync(PostCreateSetupContext request, Action saveAndRefresh)
    {
        bool hasPackages = request.SelectedInstallPackages.Count > 0;
        bool internetBlocked = request is { FirewallSettingsChanged: true, AllowInternet: false };

        bool needsProgress = request.SettingsImportPath != null
                             || request.FirewallSettingsChanged
                             || (hasPackages && internetBlocked);

        if (needsProgress)
        {
            await progressRunner.RunAsync(async progress =>
            {
                try
                {
                    if (request.SettingsImportPath != null)
                    {
                        progress.SetStatus($"Importing desktop settings for {request.NewUsername}...");
                        try
                        {
                            var importResult = await SettingsImportHelper.ImportAsync(
                                request.SettingsImportPath, request.CreatedSid,
                                settingsTransferService);
                            if (importResult.Status != SettingsImportStatus.Succeeded)
                                request.Errors.Add($"Settings import: {string.Join("; ", importResult.Errors)}");
                        }
                        catch (Exception ex)
                        {
                            request.Errors.Add($"Settings import: {ex.Message}");
                        }
                    }

                    saveAndRefresh();

                    if (hasPackages && internetBlocked && !progress.CancellationToken.IsCancellationRequested)
                    {
                        var refreshedSession = sessionProvider.GetSession();
                        var credEntry = refreshedSession.CredentialStore.Credentials.FirstOrDefault(c =>
                            string.Equals(c.Sid, request.CreatedSid, StringComparison.OrdinalIgnoreCase));
                        if (credEntry != null)
                        {
                            progress.SetStatus($"Installing packages for {request.NewUsername}...");
                            try
                            {
                                var warning = await packageInstallService.InstallPackagesAsync(
                                    request.SelectedInstallPackages,
                                    new AccountLaunchIdentity(request.CreatedSid),
                                    progress.CancellationToken);
                                var formattedWarning = LaunchExecutionWarningFormatter.Format("The package installer", warning);
                                if (formattedWarning != null)
                                    request.Warnings.Add(formattedWarning);
                                progress.SetStatus($"Waiting for install scripts to complete for {request.NewUsername}...");
                                await packageInstallService.WaitForInstallCompletionAsync(
                                    request.CreatedSid, ct: progress.CancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                log.Info($"Package install wait cancelled by user for {request.NewUsername}.");
                            }
                            catch (Exception ex)
                            {
                                request.Errors.Add($"Install scripts: {ex.Message}");
                            }
                        }
                    }

                    if (request.FirewallSettingsChanged)
                    {
                        progress.SetStatus($"Applying firewall rules for {request.NewUsername}...");
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
            });
        }
        else
        {
            saveAndRefresh();
        }
    }
}

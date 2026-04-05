using System.Security;
using RunFence.Account;
using RunFence.Infrastructure;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Launch;
using RunFence.PrefTrans;

namespace RunFence.Wizard;

/// <summary>
/// Shared post-creation setup logic for wizard templates that create accounts.
/// Handles credential storage, SidNames update, AccountEntry configuration,
/// firewall rules, desktop settings import, and install packages — in the correct order.
/// This mirrors the post-creation flow in <see cref="Account.UI.AccountCreationOrchestrator"/>,
/// but is designed for use from async wizard template executors.
/// </summary>
public class WizardAccountSetupHelper(
    IAccountCredentialManager credentialManager,
    ILocalUserProvider localUserProvider,
    ISidNameCacheService sidNameCache,
    ISettingsTransferService settingsTransferService,
    IFirewallService firewallService,
    AccountLauncher accountLauncher,
    SessionContext session)
{
    /// <summary>
    /// Parameters for post-creation account setup.
    /// </summary>
    public record SetupRequest(
        string Sid,
        string Username,
        SecureString Password,
        bool StoreCredential,
        bool IsEphemeral,
        bool SplitTokenOptOut,
        bool LowIntegrityDefault,
        FirewallAccountSettings? FirewallSettings,
        string? DesktopSettingsPath,
        List<InstallablePackage>? InstallPackages,
        bool TrayTerminal);

    /// <summary>
    /// Performs all post-creation setup steps:
    /// credential storage, SidNames, AccountEntry properties, firewall settings,
    /// desktop settings import, and install packages (respecting the install-before-firewall ordering).
    /// Returns the credential ID if a credential was stored, otherwise null.
    /// Non-fatal errors are reported via <paramref name="progress"/>.
    /// </summary>
    public async Task<Guid?> SetupAsync(SetupRequest request, IWizardProgressReporter progress)
    {
        // 1. Store credential (only when password is non-empty and StoreCredential is requested)
        Guid? credId = null;
        if (request.StoreCredential)
        {
            credId = credentialManager.StoreCreatedUserCredential(
                request.Sid, request.Password, session.CredentialStore, session.PinDerivedKey);
        }

        localUserProvider.InvalidateCache();

        // 2. Update SidNames map
        sidNameCache.ResolveAndCache(request.Sid, request.Username);

        // 3. Configure AccountEntry properties
        var entry = session.Database.GetOrCreateAccount(request.Sid);
        if (request.IsEphemeral)
            entry.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        entry.SplitTokenOptOut = request.SplitTokenOptOut;
        if (request.LowIntegrityDefault)
            entry.LowIntegrityDefault = true;
        if (request.TrayTerminal)
            entry.TrayTerminal = true;

        // 4. Store firewall settings in DB (rules applied later, after optional install)
        if (request.FirewallSettings is { IsDefault: false })
            FirewallAccountSettings.UpdateOrRemove(session.Database, request.Sid, request.FirewallSettings);

        // 5. Import desktop settings (async, non-fatal)
        if (!string.IsNullOrEmpty(request.DesktopSettingsPath) && File.Exists(request.DesktopSettingsPath))
        {
            progress.ReportStatus($"Importing desktop settings for {request.Username}...");
            try
            {
                var creds = new LaunchCredentials(request.Password, ".", request.Username);
                var (error, _) = await SettingsImportHelper.ImportAsync(
                    request.DesktopSettingsPath, creds, request.Sid,
                    settingsTransferService);
                if (error != null)
                    progress.ReportError($"Settings import: {error}");
            }
            catch (Exception ex)
            {
                progress.ReportError($"Settings import: {ex.Message}");
            }
        }

        // 6. Install packages + wait for completion before firewall (when internet is blocked)
        bool internetBlocked = request.FirewallSettings is { AllowInternet: false };
        if (request.InstallPackages?.Count > 0 && internetBlocked)
        {
            progress.ReportStatus("Installing packages (internet will be blocked after completion)...");
            var credEntry = session.CredentialStore.Credentials.FirstOrDefault(c => c.Sid == request.Sid);
            if (credEntry != null)
            {
                accountLauncher.InstallPackages(request.InstallPackages, request.Password,
                    credEntry, session.Database.SidNames);
                await accountLauncher.WaitForInstallCompletionAsync(request.Sid, TimeSpan.FromMinutes(10));
            }
        }

        // 7. Apply firewall rules (async, non-fatal)
        if (request.FirewallSettings is { IsDefault: false })
        {
            progress.ReportStatus($"Applying firewall rules for {request.Username}...");
            var username = session.Database.SidNames.GetValueOrDefault(request.Sid) ?? request.Username;
            var fwSettings = session.Database.GetAccount(request.Sid)?.Firewall ?? new FirewallAccountSettings();
            try
            {
                await Task.Run(() => firewallService.ApplyFirewallRules(request.Sid, username, fwSettings));
            }
            catch (Exception ex)
            {
                progress.ReportError($"Firewall rules: {ex.Message}");
            }
        }

        // 8. Install packages fire-and-forget (when internet is NOT blocked — no need to wait)
        if (request.InstallPackages?.Count > 0 && !internetBlocked)
        {
            var credEntry = session.CredentialStore.Credentials.FirstOrDefault(c => c.Sid == request.Sid);
            if (credEntry != null)
                accountLauncher.InstallPackages(request.InstallPackages, request.Password,
                    credEntry, session.Database.SidNames);
        }

        return credId;
    }
}
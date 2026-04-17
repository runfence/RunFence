using System.Security;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Infrastructure;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
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
    FirewallApplyHelper firewallApplyHelper,
    PackageInstallService packageInstallService,
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
        PrivilegeLevel PrivilegeLevel,
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
        entry.PrivilegeLevel = request.PrivilegeLevel;
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
                var (error, _) = await SettingsImportHelper.ImportAsync(
                    request.DesktopSettingsPath, request.Sid,
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
            if (session.CredentialStore.Credentials.Any(c => string.Equals(c.Sid, request.Sid, StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Run(() => packageInstallService.InstallPackages(request.InstallPackages, new AccountLaunchIdentity(request.Sid)));
                await packageInstallService.WaitForInstallCompletionAsync(request.Sid, TimeSpan.FromMinutes(10));
            }
        }

        // 7. Apply firewall rules (async, non-fatal)
        if (request.FirewallSettings is { IsDefault: false })
        {
            progress.ReportStatus($"Applying firewall rules for {request.Username}...");
            var username = session.Database.SidNames.GetValueOrDefault(request.Sid) ?? request.Username;
            var fwSettings = session.Database.GetAccount(request.Sid)?.Firewall ?? new FirewallAccountSettings();
            await firewallApplyHelper.ApplyWithRollbackAsync(
                sid: request.Sid,
                username: username,
                previous: null,
                final: fwSettings,
                database: session.Database,
                saveAction: () => { },
                reportError: progress.ReportError);
        }

        // 8. Install packages fire-and-forget (when internet is NOT blocked — no need to wait)
        if (request.InstallPackages?.Count > 0 && !internetBlocked)
        {
            if (session.CredentialStore.Credentials.Any(c => string.Equals(c.Sid, request.Sid, StringComparison.OrdinalIgnoreCase)))
                packageInstallService.InstallPackages(request.InstallPackages, new AccountLaunchIdentity(request.Sid));
        }

        return credId;
    }

    /// <summary>
    /// Installs the given packages under <paramref name="sid"/> and waits for completion.
    /// Only installs when the account has a stored credential. Non-fatal: progress errors are
    /// reported via <paramref name="progress"/> but execution continues.
    /// Used when packages must be installed before internet is blocked (e.g., AI agent setup).
    /// </summary>
    public async Task InstallPackagesAndWaitAsync(
        List<InstallablePackage> packages,
        string sid,
        TimeSpan timeout,
        IWizardProgressReporter progress)
    {
        if (packages.Count == 0)
            return;
        if (!session.CredentialStore.Credentials.Any(c => string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase)))
            return;

        progress.ReportStatus("Installing packages (internet will be blocked after setup completes)...");
        await Task.Run(() => packageInstallService.InstallPackages(packages, new AccountLaunchIdentity(sid)));
        await packageInstallService.WaitForInstallCompletionAsync(sid, timeout);
    }

}

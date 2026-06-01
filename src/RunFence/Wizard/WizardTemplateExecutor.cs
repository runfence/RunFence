using System.Runtime.ExceptionServices;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core.Models;

namespace RunFence.Wizard;

/// <summary>
/// Encapsulates the standard execution flow shared by all account-creating wizard templates:
/// license checks, account creation, post-creation setup, pre-enforcement actions,
/// app-entry build and enforcement, post-enforcement actions, and session save.
/// Templates build a <see cref="WizardStandardFlowParams"/> record with template-specific
/// parameters and hooks, then delegate to <see cref="ExecuteAsync"/>.
/// </summary>
public class WizardTemplateExecutor(
    EditAccountDialogCreateHandler createHandler,
    WizardAccountSetupHelperFactory setupHelperFactory,
    AppEntryBuilder appEntryBuilder,
    AppEntryEnforcementCoordinator enforcementCoordinator,
    IShortcutDiscoveryService shortcutDiscovery,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    WizardLicenseChecker licenseChecker)
{
    public WizardExecutionPlan BuildExecutionPlan(WizardStandardFlowParams flowParams, string resolvedSid)
    {
        var options = flowParams.BuildOptionsFactory?.Invoke(resolvedSid) ?? [];
        var snapshot = options.ToList().AsReadOnly();
        return new WizardExecutionPlan(
            resolvedSid,
            snapshot,
            flowParams.PreEnforcementAction != null,
            flowParams.PostEnforcementAction != null,
            flowParams.CreateDesktopShortcut);
    }

    /// <summary>
    /// Executes the standard wizard flow:
    /// <list type="number">
    /// <item>Credential license check (when <see cref="WizardStandardFlowParams.Request"/> is set and credential will be stored).</item>
    /// <item>Account creation via <c>EditAccountDialogCreateHandler</c>.</item>
    /// <item>Post-creation setup via <c>WizardAccountSetupHelper</c>.</item>
    /// <item><see cref="WizardStandardFlowParams.PreEnforcementAction"/> hook.</item>
    /// <item>App-entry build and enforcement (<c>ApplyChanges</c> per entry, <c>RecomputeAllAncestorAcls</c> once after all).</item>
    /// <item><see cref="WizardStandardFlowParams.PostEnforcementAction"/> hook.</item>
    /// <item>Session save.</item>
    /// </list>
    /// Steps are skipped when the corresponding parameter is null or empty.
    /// Returns early (without saving) on critical pre-app failures such as credential-license denial
    /// or account creation failure. Non-critical per-app failures are reported and do not stop later app actions.
    /// </summary>
    public async Task ExecuteAsync(WizardStandardFlowParams flowParams, IWizardProgressReporter progress)
    {
        string sid;
        CreateAccountResult? createdAccount = null;

        if (flowParams.Request != null)
        {
            // Credential license check (only when we will store a new credential)
            if (flowParams.SetupOptions?.StoreCredential == true)
            {
                if (!licenseChecker.CheckCanAddCredential(session, progress))
                    return;
            }

            // Account creation
            createdAccount = await Task.Run(() => createHandler.Execute(flowParams.Request));
            if (createdAccount.Status != CreateAccountStatus.Succeeded)
            {
                progress.ReportError(createdAccount.ErrorMessage ?? createHandler.LastValidationError ?? "Account creation failed.");
                return;
            }

            var restrictionEntriesToReport = ShouldReportRestrictionEntries(createdAccount.RestrictionEntries)
                ? createdAccount.RestrictionEntries!
                : [];
            var restrictionFailureMessages = restrictionEntriesToReport
                .Where(e => e.Status != AccountRestrictionStatus.Succeeded)
                .Select(AccountRestrictionEntryFormatter.Format)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var err in createdAccount.Errors.Where(err => !restrictionFailureMessages.Contains(err)))
                progress.ReportError(err);

            foreach (var entry in restrictionEntriesToReport)
            {
                var message = AccountRestrictionEntryFormatter.Format(entry);
                if (entry.Status == AccountRestrictionStatus.Succeeded)
                    progress.ReportStatus(message);
                else
                    progress.ReportError(message);
            }

            sid = createdAccount.Sid;
        }
        else
        {
            // No account creation — SID is provided explicitly or remains empty
            sid = flowParams.AccountSid ?? string.Empty;
        }

        try
        {
            var executionPlan = BuildExecutionPlan(flowParams, sid);
            var createdApps = new List<AppEntry>();
            foreach (var opts in executionPlan.AppBuildOptions)
            {
                if (!licenseChecker.CheckCanAddApp(session, progress))
                    continue;

                progress.ReportStatus($"Creating app entry for {opts.Name}...");
                try
                {
                    var app = appEntryBuilder.Build(opts with { ExistingApps = session.Database.Apps });
                    session.Database.Apps.Add(app);
                    createdApps.Add(app);
                }
                catch (Exception ex)
                {
                    progress.ReportError($"App entry for {opts.Name}: {ex.Message}");
                }
            }

            if (createdApps.Count > 0)
                sessionSaver.SaveConfig();
            else if (flowParams.Request == null && !string.IsNullOrEmpty(sid))
                sessionSaver.SaveConfig();

            if (createdAccount != null)
            {
                if (flowParams.SetupOptions != null)
                {
                    if (createdAccount.Password == null)
                    {
                        progress.ReportError("Account creation did not return a password for post-creation setup.");
                        return;
                    }

                    var setupRequest = new WizardAccountSetupHelper.SetupRequest(
                        Sid: sid,
                        Username: createdAccount.Username,
                        Password: createdAccount.Password,
                        StoreCredential: flowParams.SetupOptions.StoreCredential,
                        IsEphemeral: flowParams.SetupOptions.IsEphemeral,
                        PrivilegeLevel: flowParams.SetupOptions.PrivilegeLevel,
                        FirewallSettings: flowParams.SetupOptions.FirewallSettings,
                        DesktopSettingsPath: flowParams.SetupOptions.DesktopSettingsPath,
                        InstallPackages: flowParams.SetupOptions.InstallPackages,
                        TrayTerminal: flowParams.SetupOptions.TrayTerminal,
                        WaitForInstallPackages: flowParams.SetupOptions.WaitForInstallPackages);

                    var setupHelper = setupHelperFactory.Create(session);
                    await setupHelper.SetupAsync(setupRequest, progress);
                }
            }

            // Pre-enforcement hook (folder grants, etc.)
            if (flowParams.PreEnforcementAction != null && !string.IsNullOrEmpty(sid))
            {
                try
                {
                    await flowParams.PreEnforcementAction(session, sid);
                }
                catch (Exception ex)
                {
                    progress.ReportError($"Pre-enforcement action: {ex.Message}");
                }
            }

            // App entry build and enforcement
            if (createdApps.Count > 0)
            {
                var requiresShortcutTraversalCache = createdApps.Any(app => app.ManageShortcuts);
                ShortcutTraversalCache? shortcutCache = null;
                HashSet<string>? managedSids = null;
                ExceptionDispatchInfo? managedSidCaptureFailure = null;
                if (requiresShortcutTraversalCache)
                {
                    try
                    {
                        managedSids = shortcutDiscovery.CaptureManagedSids();
                    }
                    catch (Exception ex)
                    {
                        managedSidCaptureFailure = ExceptionDispatchInfo.Capture(ex);
                    }
                }

                var enforcementResult = await Task.Run(() =>
                    enforcementCoordinator.ApplyWizardChanges(
                        createdApps,
                        session.Database.Apps,
                        app =>
                        {
                            if (!app.ManageShortcuts)
                                return new ShortcutTraversalCache([]);

                            managedSidCaptureFailure?.Throw();

                            shortcutCache ??= shortcutDiscovery.CreateTraversalCache(managedSids);
                            return shortcutCache;
                        },
                        executionPlan.CreateDesktopShortcut));

                foreach (var failure in enforcementResult.AppFailures)
                    progress.ReportError($"App entry for {failure.App.Name}: {failure.Exception.Message}");

                if (createdApps.Count > 0)
                    progress.ReportStatus("Applying ACLs...");

                if (enforcementResult.AncestorAclRecomputeFailure != null)
                    progress.ReportError($"ACL recompute: {enforcementResult.AncestorAclRecomputeFailure.Message}");
            }

            if (flowParams.PostEnforcementAction != null && createdApps.Count > 0)
            {
                try
                {
                    await flowParams.PostEnforcementAction(session, createdApps.AsReadOnly());
                }
                catch (Exception ex)
                {
                    progress.ReportError($"Post-enforcement action: {ex.Message}");
                }
            }

            progress.ReportStatus("Done.");
            sessionSaver.SaveAndRefresh();
        }
        finally
        {
            createdAccount?.Password?.Dispose();
        }
    }

    private static bool ShouldReportRestrictionEntries(IReadOnlyList<AccountRestrictionEntry>? entries) =>
        entries is { Count: > 0 } && entries.Any(entry => entry.Status != AccountRestrictionStatus.Succeeded);
}

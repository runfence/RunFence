using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Apps;
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
    AppEntryEnforcementHelper enforcementHelper,
    IAclService aclService,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    WizardLicenseChecker licenseChecker)
{
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
    /// Returns early (without saving) only on fatal errors (account creation failure).
    /// </summary>
    public async Task ExecuteAsync(WizardStandardFlowParams flowParams, IWizardProgressReporter progress)
    {
        string sid;
        Guid? credId = flowParams.ExistingCredentialId;

        if (flowParams.Request != null)
        {
            // Credential license check (only when we will store a new credential)
            if (flowParams.SetupOptions?.StoreCredential == true)
            {
                if (!licenseChecker.CheckCanAddCredential(session, progress))
                    return;
            }

            // Account creation
            var result = await Task.Run(() => createHandler.Execute(flowParams.Request));
            if (result == null)
            {
                progress.ReportError(createHandler.LastValidationError ?? "Account creation failed.");
                return;
            }

            foreach (var err in result.Errors)
                progress.ReportError(err);

            sid = result.Sid;

            // Post-creation setup
            if (flowParams.SetupOptions != null)
            {
                var setupRequest = new WizardAccountSetupHelper.SetupRequest(
                    Sid: sid,
                    Username: result.Username,
                    Password: result.Password,
                    StoreCredential: flowParams.SetupOptions.StoreCredential,
                    IsEphemeral: flowParams.SetupOptions.IsEphemeral,
                    SplitTokenOptOut: flowParams.SetupOptions.SplitTokenOptOut,
                    LowIntegrityDefault: flowParams.SetupOptions.LowIntegrityDefault,
                    FirewallSettings: flowParams.SetupOptions.FirewallSettings,
                    DesktopSettingsPath: flowParams.SetupOptions.DesktopSettingsPath,
                    InstallPackages: flowParams.SetupOptions.InstallPackages,
                    TrayTerminal: flowParams.SetupOptions.TrayTerminal);

                var setupHelper = setupHelperFactory.Create(session);
                await setupHelper.SetupAsync(setupRequest, progress);
            }
        }
        else
        {
            // No account creation — SID is provided explicitly or remains empty
            sid = flowParams.AccountSid ?? string.Empty;
        }

        // Resolve the list of app entries to build (factory called with the resolved SID)
        var buildOptionsList = flowParams.BuildOptionsFactory?.Invoke(sid);

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
        var createdApps = new List<AppEntry>();
        if (buildOptionsList is { Count: > 0 })
        {
            foreach (var opts in buildOptionsList)
            {
                // Per-entry license check (app count grows as entries are added)
                if (!licenseChecker.CheckCanAddApp(session, progress))
                    break;

                progress.ReportStatus($"Creating app entry for {opts.Name}...");
                try
                {
                    var app = appEntryBuilder.Build(opts);
                    session.Database.Apps.Add(app);
                    createdApps.Add(app);

                    await Task.Run(() =>
                    {
                        enforcementHelper.ApplyChanges(app, session.Database.Apps, session.Database.SidNames);
                        if (flowParams.CreateDesktopShortcut)
                            enforcementHelper.CreateDesktopShortcut(app);
                    });
                }
                catch (Exception ex)
                {
                    progress.ReportError($"App entry for {opts.Name}: {ex.Message}");
                }
            }

            if (createdApps.Count > 0)
            {
                progress.ReportStatus("Applying ACLs...");
                try
                {
                    await Task.Run(() => aclService.RecomputeAllAncestorAcls(session.Database.Apps));
                }
                catch (Exception ex)
                {
                    progress.ReportError($"ACL recompute: {ex.Message}");
                }
            }
        }

        // Post-enforcement hook (handler registration, etc.)
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
}
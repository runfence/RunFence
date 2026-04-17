using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Handles local account creation from the accounts grid, including
/// the create-user dialog flow, ephemeral account registration, and auto-set of
/// associations for new users.
/// </summary>
/// <remarks>Deps above threshold: reviewed 2026-04-09. Deletion extracted to <c>AccountDeletionOrchestrator</c>.</remarks>
public class AccountCreationOrchestrator(
    IAccountCreationCommitService commitService,
    IAccountLoginRestrictionService accountRestriction,
    ISessionProvider sessionProvider,
    Func<EditAccountDialog> editAccountDialogFactory,
    IEvaluationLimitHelper evaluationLimitHelper,
    AccountPostCreateSetupService postCreateSetup,
    ToolLauncher launchService,
    ILoggingService log)
{
    private Control? _ownerControl;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    /// <summary>
    /// Binds the orchestrator to the owner control used as the dialog parent.
    /// Must be called from <see cref="Forms.AccountsPanel.BuildDynamicContent"/> before any operations.
    /// </summary>
    public void Initialize(Control ownerControl)
    {
        _ownerControl = ownerControl;
    }

    public async Task OpenCreateUserDialog(string? prefillUsername = null, string? prefillPassword = null)
    {
        try
        {
            var session = sessionProvider.GetSession();
            if (!evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
                    extraMessage: "Right-click any credential in the list to remove it."))
                return;

            var hiddenCount = session.CredentialStore.Credentials.Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
            using var dlg = editAccountDialogFactory();
            dlg.InitializeForCreate(prefillUsername, prefillPassword, hiddenCount);
            if (dlg.ShowDialog(_ownerControl?.FindForm()) != DialogResult.OK)
                return;

            var password = dlg.CreatedPassword;
            try
            {
                session = sessionProvider.GetSession();
                var commitResult = commitService.Commit(dlg, session.Database);

                if (commitResult == null)
                    return;

                bool hasPackages = dlg.SelectedInstallPackages.Count > 0;
                bool internetBlocked = dlg is { FirewallSettingsChanged: true, AllowInternet: false };

                var setupRequest = new PostCreateSetupRequest(
                    SettingsImportPath: dlg.SettingsImportPath,
                    CreatedSid: dlg.CreatedSid!,
                    NewUsername: dlg.NewUsername!,
                    FirewallSettingsChanged: dlg.FirewallSettingsChanged,
                    SelectedInstallPackages: hasPackages && internetBlocked
                        ? dlg.SelectedInstallPackages.ToList()
                        : [],
                    AllowInternet: dlg.AllowInternet,
                    Errors: dlg.Errors);

                await postCreateSetup.RunPostCreateSetupAsync(
                    setupRequest,
                    () => SaveAndRefreshRequested?.Invoke(commitResult.CredId, -1));

                if (hasPackages && !internetBlocked)
                {
                    launchService.InstallPackages(dlg.SelectedInstallPackages.ToList(), new AccountLaunchIdentity(dlg.CreatedSid!));
                }

                if (dlg.Errors.Count > 0)
                {
                    var msg = "Account created and credential stored, but some options failed:\n\n"
                              + string.Join("\n", dlg.Errors.Select(e => "\u2022 " + e));
                    MessageBox.Show(msg, "Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                if (commitResult.ShowFirstAccountWarning)
                {
                    MessageBox.Show(
                        "Some features (e.g. opening URLs) may not work until the account has been logged into at least once.\n\n" +
                        "To do a first-time login:\n" +
                        "1. Turn on \"Logon\" for this account\n" +
                        "2. Click \"Set empty password\"\n" +
                        "3. Lock Windows (Win+L) and log in as the new account, then sign out\n" +
                        "4. Come back here, turn \"Logon\" back off and click \"Rotate account password\"",
                        "First-Time Login Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                if (commitResult.ShowUsersGroupWarning)
                {
                    MessageBox.Show(
                        "This account is not a member of the Users group.\n\n" +
                        "Note: Windows automatically grants Users group access at logon to all authenticated users " +
                        "regardless of explicit group membership. Programs running under this account will still " +
                        "be able to access resources that require Users group permissions.",
                        "Users Group Note", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                password?.Dispose();
            }
        }
        catch (Exception ex)
        {
            log.Error("OpenCreateUserDialog failed unexpectedly", ex);
            MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

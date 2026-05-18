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
    AccountCreationRollbackService rollbackService,
    IAccountLoginRestrictionService accountRestriction,
    ISessionProvider sessionProvider,
    Func<IAccountCreationDialog> creationDialogFactory,
    IEvaluationLimitHelper evaluationLimitHelper,
    AccountPostCreateSetupService postCreateSetup,
    ToolLauncher launchService,
    IAccountMessageBoxService messageBoxService,
    ILoggingService log)
{
    private IAccountsPanelOperationContext _panelContext = null!;

    /// <summary>
    /// Binds the orchestrator to the account-panel operation context used for dialog ownership,
    /// saving, and refresh.
    /// Must be called from <see cref="Forms.AccountsPanel.BuildDynamicContent"/> before any operations.
    /// </summary>
    public void Initialize(IAccountsPanelOperationContext panelContext)
    {
        _panelContext = panelContext;
    }

    public async Task OpenCreateUserDialog(string? prefillUsername = null, ProtectedString? prefillPassword = null)
    {
        try
        {
            var session = sessionProvider.GetSession();
            if (!evaluationLimitHelper.CheckCredentialLimit(
                    session.CredentialStore.Credentials,
                    extraMessage: "Right-click any credential in the list to remove it."))
            {
                return;
            }

            var hiddenCount = session.CredentialStore.Credentials.Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
            using var dlg = creationDialogFactory();
            var completionState = new CreateDialogCompletionState();
            dlg.CreateConfirmRequested += () => ConfirmCreatedAccountAsync(dlg, completionState);
            dlg.InitializeForCreate(prefillUsername, prefillPassword, hiddenCount);
            if (await dlg.ShowCreateDialogAsync(_panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl) != DialogResult.OK)
                return;

            try
            {
                if (completionState.ShouldStopAfterClose)
                    return;

                if (completionState.CommittedResult == null
                    || completionState.CreatedSid == null
                    || completionState.CreatedUsername == null)
                {
                    throw new InvalidOperationException("Missing captured account creation state after dialog confirmation.");
                }

                if (completionState.ShouldRunPostCreateSetup)
                {
                    var setupRequest = new PostCreateSetupContext(
                        SettingsImportPath: completionState.SettingsImportPath,
                        CreatedSid: completionState.CreatedSid,
                        NewUsername: completionState.CreatedUsername,
                        FirewallSettingsChanged: completionState.FirewallSettingsChanged,
                        SelectedInstallPackages: completionState.HasPackages && completionState.InternetBlocked
                            ? completionState.SelectedInstallPackages
                            : [],
                        AllowInternet: completionState.AllowInternet,
                        Errors: completionState.CreateErrors,
                        Warnings: completionState.PostCreateWarnings);

                    await postCreateSetup.RunPostCreateSetupAsync(
                        setupRequest,
                        () => _panelContext.SaveAndRefresh(completionState.CommittedResult.CredId, -1));
                }

                if (completionState.HasPackages && !completionState.InternetBlocked)
                {
                    launchService.InstallPackages(
                        completionState.SelectedInstallPackages,
                        new AccountLaunchIdentity(completionState.CreatedSid));
                }

                if (completionState.CreateErrors.Count > 0 || completionState.PostCreateWarnings.Count > 0)
                {
                    var sections = new List<string>();
                    if (completionState.CreateErrors.Count > 0)
                        sections.Add("Errors:\n" + string.Join("\n", completionState.CreateErrors.Select(e => "\u2022 " + e)));
                    if (completionState.PostCreateWarnings.Count > 0)
                        sections.Add("Warnings:\n" + string.Join("\n", completionState.PostCreateWarnings.Select(w => "\u2022 " + w)));
                    var msg = completionState.CreateErrors.Count == 0
                        ? "Account created, but some work completed with warnings:\n\n"
                        : completionState.PostCreateWarnings.Count == 0
                            ? "Account created, but some work failed:\n\n"
                            : "Account created, but some work failed and some completed with warnings:\n\n";
                    msg += string.Join("\n\n", sections);
                    messageBoxService.Show(
                        _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                        msg,
                        "Warnings",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                if (completionState.CommittedResult.ShowFirstAccountWarning)
                {
                    messageBoxService.Show(
                        _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
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

                if (completionState.CommittedResult.ShowUsersGroupWarning)
                {
                    messageBoxService.Show(
                        _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                        "This account is not a member of the Users group.\n\n" +
                        "Note: Windows automatically grants Users group access at logon to all authenticated users " +
                        "regardless of explicit group membership. Programs running under this account will still " +
                        "be able to access resources that require Users group permissions.",
                        "Users Group Note",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            finally
            {
                completionState.TransferredPassword?.Dispose();
            }
        }
        catch (Exception ex)
        {
            log.Error("OpenCreateUserDialog failed unexpectedly", ex);
            messageBoxService.Show(
                _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                $"An unexpected error occurred: {ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            prefillPassword?.Dispose();
        }
    }

    private async Task<bool> ConfirmCreatedAccountAsync(IAccountCreationDialog dlg, CreateDialogCompletionState state)
    {
        try
        {
            return await ConfirmCreatedAccountCoreAsync(dlg, state);
        }
        catch (Exception ex)
        {
            log.Error("Account creation confirmation failed unexpectedly", ex);
            messageBoxService.Show(
                _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                $"An unexpected error occurred while saving the created account: {ex.Message}",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task<bool> ConfirmCreatedAccountCoreAsync(IAccountCreationDialog dlg, CreateDialogCompletionState state)
    {
        state.CaptureDialogState(dlg);

        if (dlg.CreatedAccountStatus == CreateAccountStatus.CleanupStateSaveFailed)
        {
            state.ShouldStopAfterClose = true;
            messageBoxService.Show(
                _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                "Windows created the account, but RunFence could not save its cleanup state.\n\n" +
                "The account remains in memory for this session only:\n" +
                dlg.CreatedAccountErrorMessage,
                "Account Created But Not Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _panelContext.RefreshAndNotifyDataChanged(null, -1);
            return true;
        }

        var session = sessionProvider.GetSession();
        var commitData = new AccountCreationData(
            CreatedSid: dlg.CreatedSid!,
            CreatedPassword: dlg.CreatedPassword!,
            NewUsername: dlg.NewUsername!,
            IsEphemeral: dlg.IsEphemeral,
            PrivilegeLevel: dlg.SelectedPrivilegeLevel,
            FirewallSettingsChanged: dlg.FirewallSettingsChanged,
            AllowInternet: dlg.AllowInternet,
            AllowLocalhost: dlg.AllowLocalhost,
            AllowLan: dlg.AllowLan,
            UsersGroupUnchecked: dlg.UsersGroupUnchecked,
            AdminGroupChecked: dlg.AdminGroupChecked,
            CreationRollbackState: dlg.CreatedRollbackState);
        var commitOutcome = commitService.Commit(commitData, session.Database);

        if (commitOutcome.Status == AccountCreationCommitStatus.DuplicateCredential)
        {
            state.ShouldStopAfterClose = true;
            state.TransferredPassword = dlg.CreatedPassword;
            return true;
        }

        bool hasPendingSetup =
            state.SettingsImportPath != null ||
            state.FirewallSettingsChanged ||
            state.HasPackages;

        switch (commitOutcome.Status)
        {
            case AccountCreationCommitStatus.SaveFailedAfterMutation:
                if (commitOutcome.RollbackState == null)
                    throw new InvalidOperationException("Missing commit rollback details for failed account creation.");

                if (hasPendingSetup || commitOutcome.Result == null)
                {
                    try
                    {
                        await rollbackService.RollbackAsync(
                            commitOutcome.RollbackState,
                            session.Database,
                            session.CredentialStore);
                    }
                    catch (Exception rollbackEx)
                    {
                        session.Database.GetOrCreateAccount(dlg.CreatedSid!).DeleteAfterUtc = DateTime.UtcNow.AddHours(1);
                        _panelContext.RefreshAndNotifyDataChanged(commitOutcome.Result?.CredId, -1);
                        log.Error("AccountCreationOrchestrator rollback failed after save boundary failure", rollbackEx);
                        messageBoxService.Show(
                            _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                            "Windows created the account, but RunFence could not save the updated credential/config state and rollback also failed.\n\n" +
                            $"Save error: {commitOutcome.ErrorMessage}\n" +
                            $"Rollback error: {rollbackEx.Message}",
                            "Account Creation Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        state.ShouldStopAfterClose = true;
                        state.TransferredPassword = dlg.CreatedPassword;
                        return true;
                    }

                    messageBoxService.Show(
                        _panelContext.OwnerControl.FindForm() ?? _panelContext.OwnerControl,
                        "Windows created the account, but RunFence could not save the updated credential/config state before the remaining setup steps.\n\n" +
                        "The account was rolled back:\n" +
                        commitOutcome.ErrorMessage,
                        "Account Creation Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }

                state.CommittedResult = commitOutcome.Result;
                state.PostCreateWarnings.Add(
                    "RunFence could not save the updated credential/config state. " +
                    "The created account remains available only in memory for this session:\n" +
                    commitOutcome.ErrorMessage);
                _panelContext.RefreshAndNotifyDataChanged(state.CommittedResult.CredId, -1);
                state.ShouldRunPostCreateSetup = false;
                state.TransferredPassword = dlg.CreatedPassword;
                return true;

            case AccountCreationCommitStatus.Succeeded:
                if (commitOutcome.Result == null)
                    throw new InvalidOperationException("Missing commit result for successful account creation.");

                state.CommittedResult = commitOutcome.Result;
                state.ShouldRunPostCreateSetup = true;
                state.TransferredPassword = dlg.CreatedPassword;
                return true;

            default:
                throw new InvalidOperationException($"Unexpected commit status: {commitOutcome.Status}.");
        }
    }

    private sealed class CreateDialogCompletionState
    {
        public ProtectedString? TransferredPassword { get; set; }
        public AccountCreationCommitResult? CommittedResult { get; set; }
        public List<string> PostCreateWarnings { get; private set; } = [];
        public bool ShouldStopAfterClose { get; set; }
        public bool ShouldRunPostCreateSetup { get; set; }
        public bool HasPackages { get; private set; }
        public bool InternetBlocked { get; private set; }
        public string? CreatedSid { get; private set; }
        public string? CreatedUsername { get; private set; }
        public string? SettingsImportPath { get; private set; }
        public bool FirewallSettingsChanged { get; private set; }
        public bool AllowInternet { get; private set; } = true;
        public List<string> CreateErrors { get; private set; } = [];
        public List<InstallablePackage> SelectedInstallPackages { get; private set; } = [];

        public void CaptureDialogState(IAccountCreationDialog dlg)
        {
            CreatedSid = dlg.CreatedSid;
            CreatedUsername = dlg.NewUsername;
            SettingsImportPath = dlg.SettingsImportPath;
            FirewallSettingsChanged = dlg.FirewallSettingsChanged;
            AllowInternet = dlg.AllowInternet;
            CreateErrors = [.. dlg.Errors];
            SelectedInstallPackages = [.. dlg.SelectedInstallPackages];
            HasPackages = SelectedInstallPackages.Count > 0;
            InternetBlocked = FirewallSettingsChanged && !AllowInternet;
            PostCreateWarnings = [];
            CommittedResult = null;
            ShouldStopAfterClose = false;
            ShouldRunPostCreateSetup = false;
            TransferredPassword = null;
        }
    }
}

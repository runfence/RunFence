using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Apps.UI;
using RunFence.Infrastructure;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for setting up a dedicated gaming account.
/// Supports two modes:
/// <list type="bullet">
///   <item>Create new account — inserts AccountNameStep dynamically after the account picker.</item>
///   <item>Use existing account — if the account has no stored credential, shows
///         <see cref="CredentialEditDialog"/> on the secure desktop before proceeding.</item>
/// </list>
/// In both cases, grants full ownership of game install folders, creates denied-execute app entries
/// for game launchers, and skips items that already exist (without removing previously added ones).
/// </summary>
public class GamingAccountTemplate(
    WizardTemplateExecutor executor,
    WizardTemplateSetupBuilder setupBuilder,
    SessionContext session,
    GamingExistingAccountPreparationService existingAccountPreparationService,
    WizardAccountPickerService pickerStepService,
    WizardCredentialCollector credentialCollector,
    GamingWizardStepBuilder stepBuilder)
    : IWizardTemplate
{
    private readonly GamingAccountTemplateState _data = new();

    public string DisplayName => "Gaming Account";
    public string Description => "Isolated gaming account with full access to game folders and launcher shortcuts";
    public string IconEmoji => "\U0001F3AE"; // 🎮
    public Func<IWin32Window, Task>? PostWizardAction => null;

    public void Cleanup()
    {
        _data.DisposeSecrets();
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();

        string? interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        var data = _data;

        var pickerStep = pickerStepService.CreatePickerStep(
            setSelection: (sid, isCreate) =>
            {
                data.ExistingAccountSid = sid;
                data.IsExistingAccount = !isCreate;
            },
            options: new AccountPickerStepOptions(
                Credentials: session.CredentialStore.Credentials,
                SidNames: session.Database.SidNames,
                GroupSid: GroupFilterHelper.UsersSid,
                StepTitle: "Gaming Account",
                InfoText: "Select an existing user account to use as the gaming account, or choose " +
                          "\"Create new account\" to create a dedicated one. " +
                          "Accounts with a green dot have stored credentials; gray dot means you will be prompted for a password.",
                InteractiveUserSid: interactiveSid,
                ExcludeAdmins: true,
                DefaultToCreateNew: true),
            followingStepsFactory: isCreate =>
            {
                var instructionsStep = stepBuilder.CreateInstructionsStep(isCreateNew: isCreate);
                var foldersStep = stepBuilder.CreateFoldersStep(paths => data.GameFolders = paths);
                var launchersStep = stepBuilder.CreateLaunchersStep(
                    launchers => data.GameLaunchers = launchers,
                    getSid: () => data.IsExistingAccount ? data.ExistingAccountSid : null);

                if (isCreate)
                {
                    var nameStep = stepBuilder.CreateAccountNameStep(
                        (name, password) =>
                        {
                            data.Username = name;
                            data.Password?.Dispose();
                            data.Password = password;
                        },
                        showPassword: true,
                        requirePassword: true,
                        description: "Choose a name and password for the gaming account. " +
                                     "A password is required because the account needs to log in interactively via Win+L " +
                                     "so you can install and update games from their launchers.");
                    return [instructionsStep, nameStep, foldersStep, launchersStep];
                }

                return [instructionsStep, foldersStep, launchersStep];
            },
            commitAction: progress =>
            {
                if (!_data.IsExistingAccount || string.IsNullOrEmpty(_data.ExistingAccountSid))
                    return Task.CompletedTask;
                var pw = credentialCollector.CollectCredentialForStep(_data.ExistingAccountSid, progress);
                if (pw != null) _data.CollectedPassword = pw;
                return Task.CompletedTask;
            });

        return [pickerStep];
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (_data.IsExistingAccount)
            await ExecuteForExistingAccountAsync(progress);
        else
            await ExecuteForNewAccountAsync(progress);
    }

    private async Task ExecuteForNewAccountAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            return;
        }

        progress.ReportStatus($"Creating account '{_data.Username}'...");
        var flowParams = setupBuilder.BuildGamingNewAccountFlow(_data, progress);

        try
        {
            await executor.ExecuteAsync(flowParams, progress);
        }
        finally
        {
            flowParams.Request?.Password.Dispose();
            flowParams.Request?.ConfirmPassword.Dispose();
        }
    }

    private async Task ExecuteForExistingAccountAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.ExistingAccountSid))
        {
            progress.ReportError("No account was selected.");
            return;
        }

        var sid = _data.ExistingAccountSid;
        if (!existingAccountPreparationService.Prepare(
                session,
                sid,
                _data.CollectedPassword,
                progress))
            return;

        await executor.ExecuteAsync(setupBuilder.BuildGamingExistingAccountFlow(_data, progress), progress);
    }
}

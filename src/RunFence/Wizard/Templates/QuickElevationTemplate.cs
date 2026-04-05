using Microsoft.Win32;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template that creates a single-character administrator account for fast UAC elevation.
/// No stored credential is created (empty password). The account is hidden from the logon screen
/// and UAC administrator enumeration is suppressed so only the new account name is shown.
/// </summary>
public class QuickElevationTemplate(
    EditAccountDialogCreateHandler createHandler,
    ILocalUserProvider localUserProvider,
    ILocalGroupMembershipService groupMembershipService,
    IAccountRestrictionService accountRestriction,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    ILicenseService licenseService,
    ISidNameCacheService sidNameCache)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Quick Elevation (UAC)";
    public string Description => "Create a 1-character admin account for fast UAC prompts.";
    public string IconEmoji => "\U0001F510";
    public Action<IWin32Window>? PostWizardAction => null;

    public void Cleanup()
    {
    }

    /// <summary>
    /// Hidden when a 1-character administrator account already exists with no password
    /// (or no stored credential, which implies no password for this account type).
    /// </summary>
    public bool IsAvailable => !HasExistingQuickElevationAccount();

    private bool HasExistingQuickElevationAccount()
    {
        try
        {
            var admins = groupMembershipService.GetMembersOfGroup(GroupFilterHelper.AdministratorsSid);
            foreach (var admin in admins)
            {
                if (admin.Username.Length != 1)
                    continue;

                var cred = session.CredentialStore.Credentials
                    .FirstOrDefault(c => string.Equals(c.Sid, admin.Sid, StringComparison.OrdinalIgnoreCase));

                // No credential stored → assume empty password; stored credential with empty password → qualifies.
                // Stored credential with non-empty password → does not qualify (different account type).
                if (cred == null || cred.EncryptedPassword.Length == 0)
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<WizardStepPage> CreateSteps() =>
    [
        new AccountNameStep(
            (name, _) => _data.Username = name,
            showPassword: false,
            maxNameLength: 1,
            description: "This creates a dedicated administrator account with a single-character name. " +
                         "At UAC prompts, type just this one character and press Enter — much faster than a full password.",
            accountExists: name => localUserProvider.GetLocalUserAccounts()
                .Any(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase)))
    ];

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            return;
        }

        // License check — hiding accounts requires a valid license
        var hiddenCount = CountHiddenAccounts();
        if (!licenseService.CanHideAccount(hiddenCount))
        {
            progress.ReportError(licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, hiddenCount)!);
            return;
        }

        progress.ReportStatus("Creating administrator account...");

        // Blank password is intentional for UAC elevation accounts. Windows'
        // LimitBlankPasswordUse policy (HKLM\SYSTEM\CurrentControlSet\Control\Lsa) blocks
        // blank-password accounts from network and interactive logon, but UAC elevation
        // ("Run as administrator" from a different admin account) is unaffected by this policy.
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: _data.Username,
            PasswordText: string.Empty,
            ConfirmPasswordText: string.Empty,
            IsEphemeral: false,
            CheckedGroups: [(GroupFilterHelper.AdministratorsSid, "Administrators")],
            UncheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
            AllowLogon: false,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            CurrentHiddenCount: hiddenCount);

        var result = await Task.Run(() => createHandler.Execute(request));
        if (result == null)
        {
            progress.ReportError(createHandler.LastValidationError ?? "Account creation failed.");
            return;
        }

        foreach (var err in result.Errors)
            progress.ReportError(err);

        // Hide from logon screen
        progress.ReportStatus("Hiding account from logon screen...");
        try
        {
            accountRestriction.SetAccountHidden(result.Username, result.Sid, true);
        }
        catch (Exception ex)
        {
            progress.ReportError($"Hide account: {ex.Message}");
        }

        // Suppress admin enumeration in UAC prompts
        progress.ReportStatus("Configuring UAC to hide admin list...");
        try
        {
            // Store the original value only on the first wizard run so we can restore it later.
            if (session.Database.Settings.OriginalUacAdminEnumeration == null)
            {
                session.Database.Settings.OriginalUacAdminEnumeration = ReadCurrentUacAdminEnumeration();
                session.Database.Settings.UacAdminEnumerationSid = result.Sid;
            }

            accountRestriction.SetUacAdminEnumeration(false);
        }
        catch (Exception ex)
        {
            progress.ReportError($"UAC enumeration setting: {ex.Message}");
        }

        // Update SidNames
        sidNameCache.UpdateName(result.Sid, result.Username);

        progress.ReportStatus($"Account '{result.Username}' created.");
        sessionSaver.SaveAndRefresh();
    }

    private int CountHiddenAccounts() =>
        session.CredentialStore.Credentials
            .Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));

    /// <summary>
    /// Reads the current <c>EnumerateAdministrators</c> registry value.
    /// Returns the current DWORD value, or <c>-1</c> if the key/value is absent (Windows default = enumerate).
    /// The sentinel <c>-1</c> is recognized by <see cref="AccountLifecycleManager"/> as
    /// "restore by deleting the value" rather than writing a DWORD back.
    /// </summary>
    private static int ReadCurrentUacAdminEnumeration()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\CredUI");
            if (key?.GetValue("EnumerateAdministrators") is int v)
                return v;
        }
        catch
        {
        }

        return -1; // absent — sentinel meaning "delete value on restore"
    }

    private sealed class CommitData
    {
        public string Username { get; set; } = string.Empty;
    }
}
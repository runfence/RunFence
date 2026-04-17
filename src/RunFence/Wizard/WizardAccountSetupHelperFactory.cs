using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Infrastructure;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard;

/// <summary>
/// Factory for <see cref="WizardAccountSetupHelper"/> that holds the shared dependencies
/// common to all account-creating wizard templates. Templates call <see cref="Create"/>
/// at the start of <c>ExecuteAsync</c>, passing the per-run <see cref="SessionContext"/>.
/// </summary>
public class WizardAccountSetupHelperFactory(
    IAccountCredentialManager credentialManager,
    ILocalUserProvider localUserProvider,
    ILocalGroupMembershipService groupMembership,
    ISidNameCacheService sidNameCache,
    ISettingsTransferService settingsTransferService,
    FirewallApplyHelper firewallApplyHelper,
    PackageInstallService packageInstallService,
    IDatabaseProvider databaseProvider)
{
    /// <summary>
    /// Creates an <see cref="AccountNameStep"/> with account-existence validation using the
    /// shared <see cref="IWindowsAccountService"/> without exposing it as a public property.
    /// </summary>
    public AccountNameStep CreateAccountNameStep(
        Action<string, string> onCommit,
        bool showPassword = false,
        int maxNameLength = 20,
        string? description = null,
        bool requirePassword = false) =>
        new(onCommit,
            showPassword: showPassword,
            maxNameLength: maxNameLength,
            description: description,
            requirePassword: requirePassword,
            accountExists: name => localUserProvider.GetLocalUserAccounts()
                .Any(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase)));

    public AccountCreationDefaults CreateAccountDefaults() =>
        AccountCreationDefaults.Create(databaseProvider.GetDatabase(), groupMembership);

    public WizardAccountSetupHelper Create(SessionContext session) =>
        new(credentialManager, localUserProvider, sidNameCache,
            settingsTransferService, firewallApplyHelper, packageInstallService, session);
}

using RunFence.Account;
using RunFence.Core.Models;

namespace RunFence.Wizard;

/// <summary>
/// Template-specific account setup parameters for <see cref="WizardTemplateExecutor"/>.
/// Contains everything needed to build a <see cref="WizardAccountSetupHelper.SetupRequest"/>
/// except Sid, Username and Password, which come from the account creation result.
/// </summary>
public record WizardSetupOptions(
    bool StoreCredential,
    bool IsEphemeral,
    bool SplitTokenOptOut,
    bool LowIntegrityDefault,
    FirewallAccountSettings? FirewallSettings,
    string? DesktopSettingsPath,
    List<InstallablePackage>? InstallPackages,
    bool TrayTerminal);
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Factory that creates <see cref="AccountPickerStep"/> instances for wizard templates.
/// Holds the 5 account-picker service dependencies so each template does not need to declare them.
/// </summary>
public class WizardAccountPickerStepFactory(
    ILocalGroupMembershipService groupMembership,
    ILocalUserProvider localUserProvider,
    ISidResolver sidResolver,
    IProfilePathResolver profilePathResolver,
    CredentialFilterHelper credentialFilterHelper,
    Func<WizardCredentialCollector> credentialCollectorFactory)
{
    /// <summary>
    /// Creates an <see cref="AccountPickerStep"/> with the given options and callbacks.
    /// </summary>
    /// <param name="setSelection">Receives (sid, isCreate) on Collect. sid is null when "Create new account" is chosen.</param>
    /// <param name="options">Per-call data parameters for this step instance.</param>
    /// <param name="followingStepsFactory">
    /// Called when the user switches between "Create new account" and an existing account.
    /// Receives <c>true</c> when "Create new account" is selected.
    /// If null, no dynamic step replacement occurs.
    /// </param>
    /// <param name="commitAction">
    /// Optional mid-wizard async action run after Collect and before the wizard advances.
    /// Typically used to collect credentials from the secure desktop for an existing account.
    /// </param>
    public AccountPickerStep CreatePickerStep(
        Action<string?, bool> setSelection,
        AccountPickerStepOptions options,
        Func<bool, IReadOnlyList<WizardStepPage>>? followingStepsFactory,
        Func<IWizardProgressReporter, Task>? commitAction) =>
        new(
            setSelection: setSelection,
            groupMembership: groupMembership,
            localUserProvider: localUserProvider,
            sidResolver: sidResolver,
            profilePathResolver: profilePathResolver,
            credentialFilterHelper: credentialFilterHelper,
            options: options,
            followingStepsFactory: followingStepsFactory,
            commitAction: commitAction);

    /// <summary>
    /// Creates a new <see cref="WizardCredentialCollector"/> for collecting credentials
    /// on the secure desktop when an existing account has no stored credentials.
    /// </summary>
    public WizardCredentialCollector CreateCredentialCollector() => credentialCollectorFactory();

    /// <summary>
    /// Attempts to resolve <paramref name="sid"/> to a display name.
    /// Returns null when the SID cannot be resolved.
    /// </summary>
    public string? TryResolveName(string sid) => sidResolver.TryResolveName(sid);
}

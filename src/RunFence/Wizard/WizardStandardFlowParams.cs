using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core.Models;

namespace RunFence.Wizard;

/// <summary>
/// Parameters for <see cref="WizardTemplateExecutor.ExecuteAsync"/>.
/// All fields are optional; null/empty values cause the corresponding step to be skipped.
/// </summary>
public record WizardStandardFlowParams(
    /// <summary>
    /// Account creation request. When non-null, the executor creates a new account via
    /// <c>EditAccountDialogCreateHandler</c> and uses the resulting SID for subsequent steps.
    /// </summary>
    EditAccountDialogCreateHandler.CreateAccountRequest? Request,
    /// <summary>
    /// Template-specific setup options (credential storage, account entry configuration, etc.).
    /// Requires <see cref="Request"/> to be non-null (SID and password come from account creation).
    /// When null, account setup is skipped.
    /// </summary>
    WizardSetupOptions? SetupOptions,
    /// <summary>
    /// Factory that produces the app entries to build, given the resolved account SID.
    /// The SID may come from account creation (when <see cref="Request"/> is set) or from
    /// <see cref="AccountSid"/> (when the account was created outside the executor).
    /// Returning null or an empty list skips the app-entry step.
    /// Using a factory allows templates to embed the SID into <c>AppEntryBuildOptions</c>
    /// after account creation resolves it.
    /// </summary>
    Func<string, IReadOnlyList<AppEntryBuildOptions>?>? BuildOptionsFactory = null,
    /// <summary>
    /// Pre-resolved account SID for templates that create or select an account outside the
    /// executor. Used when <see cref="Request"/> is null (no account creation inside executor).
    /// Passed to <see cref="PreEnforcementAction"/> and to <see cref="BuildOptionsFactory"/>.
    /// </summary>
    string? AccountSid = null,
    /// <summary>
    /// Optional action executed with (session, sid) after account setup and before app-entry
    /// enforcement. Used to grant folder access or perform other per-SID actions that must happen
    /// before ACLs are applied.
    /// </summary>
    Func<SessionContext, string, Task>? PreEnforcementAction = null,
    /// <summary>
    /// Optional action executed with (session, created app entries) after all app-entry
    /// enforcement is complete but before the session is saved. Used for tasks such as
    /// registering handler associations.
    /// </summary>
    Func<SessionContext, IReadOnlyList<AppEntry>, Task>? PostEnforcementAction = null,
    /// <summary>
    /// When true, the executor creates a shortcut on the interactive user's desktop for each
    /// app entry built during this flow (in addition to the standard beside-target shortcut).
    /// </summary>
    bool CreateDesktopShortcut = false);
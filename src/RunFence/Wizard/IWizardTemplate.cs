using RunFence.Wizard.UI.Forms;

namespace RunFence.Wizard;

/// <summary>
/// Defines a wizard template: metadata for display in the template picker, step pages for data
/// collection, an async executor that performs the actual operations, and an optional post-wizard
/// action to run after the wizard dialog closes (e.g., opening a follow-up dialog).
/// </summary>
public interface IWizardTemplate
{
    /// <summary>Display name shown on the template picker card.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this template should appear in the picker.
    /// Return false to suppress display when the template has no applicable targets
    /// (e.g., no data drives with replaceable ACEs for Prepare System).
    /// Default: true.
    /// </summary>
    bool IsAvailable => true;

    /// <summary>
    /// Whether this template is a prerequisite that should run before other templates.
    /// Prerequisite templates are sorted first in the picker and highlighted with an amber border.
    /// The picker shows a notice recommending they be run first.
    /// Default: false.
    /// </summary>
    bool IsPrerequisite => false;

    /// <summary>Short description shown under the display name on the template picker card.</summary>
    string Description { get; }

    /// <summary>Emoji icon shown on the template picker card.</summary>
    string IconEmoji { get; }

    /// <summary>
    /// Creates the sequence of step pages for this template.
    /// Steps receive a reference to the template's commit data at construction and write to it in
    /// <see cref="WizardStepPage.Collect"/>. Called once per wizard session when the template is selected.
    /// </summary>
    IReadOnlyList<WizardStepPage> CreateSteps();

    /// <summary>
    /// Asynchronously executes the template using the data collected by its steps.
    /// Called after the user confirms the final step. Non-fatal errors are reported via
    /// <paramref name="progress"/> — execution should proceed as far as possible.
    /// </summary>
    Task ExecuteAsync(IWizardProgressReporter progress);

    /// <summary>
    /// Optional action to run on the UI thread after the wizard dialog closes.
    /// Use for follow-up dialogs that should open after the wizard (e.g., firewall allowlist editor).
    /// Null if no post-wizard action is needed.
    /// </summary>
    Action<IWin32Window>? PostWizardAction { get; }

    /// <summary>
    /// Called when the wizard dialog is closed (normally or via cancel) to release sensitive resources
    /// such as <see cref="System.Security.SecureString"/> fields held in commit data.
    /// Templates without sensitive resources should implement this as a no-op.
    /// </summary>
    void Cleanup();

    /// <summary>
    /// Optionally pre-warms caches or performs async IO needed by <see cref="IsAvailable"/> or
    /// <see cref="CreateSteps"/>. Called concurrently for all templates before the wizard dialog
    /// is constructed so that availability checks never block the UI thread.
    /// Default: no-op.
    /// </summary>
    Task WarmCacheAsync() => Task.CompletedTask;
}
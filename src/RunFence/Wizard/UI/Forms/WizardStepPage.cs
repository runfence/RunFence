using RunFence.UI;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Abstract base for all wizard step pages. Each step is a <see cref="UserControl"/> that knows
/// its title, can validate user input, collect its data into the template's commit object,
/// and optionally run async work before the wizard advances.
/// </summary>
public abstract class WizardStepPage : UserControl
{
    protected WizardStepPage()
    {
        BackColor = Color.White;
    }

    /// <summary>Header text displayed at the top of the wizard when this step is active.</summary>
    public abstract string StepTitle { get; }

    /// <summary>
    /// Validates the current user input.
    /// Returns null if input is valid; otherwise returns the error message to show in the status label.
    /// </summary>
    public new abstract string? Validate();

    /// <summary>
    /// Writes the collected step data into the template's commit data object.
    /// Called by the wizard immediately before advancing to the next step.
    /// </summary>
    public abstract void Collect();

    /// <summary>
    /// Called when this step becomes the active (visible) step.
    /// Override to refresh dynamic UI (e.g., auto-detect paths, populate lists).
    /// </summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>
    /// Optional mid-wizard async hook. Called after <see cref="Collect"/> but before the wizard
    /// advances to the next step. Use for operations that must complete before the next step is shown
    /// (e.g., launching install scripts and waiting for completion).
    /// Default implementation returns immediately.
    /// </summary>
    public virtual Task OnCommitBeforeNextAsync(IWizardProgressReporter progress) => Task.CompletedTask;

    /// <summary>
    /// Raised when this step wants to replace all steps that follow it with a new sequence.
    /// <see cref="WizardDialog"/> subscribes to this for each step and re-slices <c>_steps</c>
    /// in response. Steps should fire this when the user makes a branching choice
    /// (e.g., switching between container mode and account mode).
    /// The event sender is the step that requested the replacement.
    /// </summary>
    public event EventHandler<IReadOnlyList<WizardStepPage>>? ReplaceFollowingSteps;

    /// <summary>
    /// Replaces all steps that follow this one in the wizard with <paramref name="newSteps"/>.
    /// Call this from subclasses when a branching choice changes which subsequent steps are relevant.
    /// </summary>
    protected void RequestReplaceFollowingSteps(IReadOnlyList<WizardStepPage> newSteps) =>
        ReplaceFollowingSteps?.Invoke(this, newSteps);

    /// <summary>
    /// Raised when <see cref="CanProceed"/> changes.
    /// <see cref="WizardDialog"/> subscribes to this to update the Next/Commit button state reactively.
    /// </summary>
    public event EventHandler? CanProceedChanged;

    /// <summary>
    /// Whether the user may proceed from this step.
    /// Default is true; override in steps where completion of mandatory input must be checked
    /// before the Next/Commit button becomes active.
    /// </summary>
    public virtual bool CanProceed => true;

    /// <summary>
    /// Call from subclasses whenever <see cref="CanProceed"/> may have changed.
    /// </summary>
    protected void NotifyCanProceedChanged() => CanProceedChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Registers a label for height-tracked text wrapping. The label must have <c>AutoSize = false</c>
    /// and <c>Dock = DockStyle.Top</c> (or Bottom); its <c>Height</c> is recomputed via
    /// <see cref="TextRenderer.MeasureText"/> whenever the step resizes so the label always fits
    /// its wrapped content without overflowing or collapsing to a single line.
    /// </summary>
    protected void TrackWrappingLabel(Label label)
    {
        Resize += (_, _) => WrappingLabelHelper.UpdateHeight(this, label);
        WrappingLabelHelper.UpdateHeight(this, label);
    }
}
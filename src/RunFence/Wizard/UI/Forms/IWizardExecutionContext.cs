namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Provides access to wizard dialog state and UI operations for
/// <see cref="WizardExecutionHandler"/> and <see cref="WizardNavigationHandler"/>.
/// </summary>
public interface IWizardExecutionContext
{
    /// <summary>Current step index (0 = template picker).</summary>
    int CurrentStepIndex { get; }

    /// <summary>All wizard step pages including the template picker at index 0.</summary>
    List<WizardStepPage> Steps { get; }

    /// <summary>The currently selected template, or null when on the picker.</summary>
    IWizardTemplate? SelectedTemplate { get; set; }

    /// <summary>All available templates shown in the picker.</summary>
    IReadOnlyList<IWizardTemplate> Templates { get; }

    /// <summary>Whether a template is currently executing.</summary>
    bool IsExecuting { get; set; }

    /// <summary>Post-wizard actions accumulated from completed templates.</summary>
    List<Action<IWin32Window>> PostWizardActions { get; }

    /// <summary>Number of templates completed during this wizard session.</summary>
    int TemplateCompletedCount { get; set; }

    /// <summary>Shows the step at the given index and updates all related UI.</summary>
    void ShowStep(int index);

    /// <summary>Shows an error message below the content area.</summary>
    void ShowError(string message);

    /// <summary>Hides and clears the error message.</summary>
    void HideError();

    /// <summary>Shows or hides the progress panel with the animated progress bar.</summary>
    void SetProgressVisible(bool visible);

    /// <summary>Enables or disables all navigation buttons.</summary>
    void SetNavigationEnabled(bool enabled);

    /// <summary>Sets the status label text, marshalling to the UI thread if needed.</summary>
    void SetStatusText(string text);

    /// <summary>Enables or disables the Next button and sets the Cancel button text. Back is unaffected.</summary>
    void SetCompletionButtonsState(bool showNavigation, string cancelText);

    /// <summary>Sets Back button enabled state.</summary>
    void SetBackEnabled(bool enabled);

    /// <summary>Sets Next button text.</summary>
    void SetNextText(string text);

    /// <summary>Invalidates the step indicator panel to trigger repaint.</summary>
    void InvalidateStepIndicator();

    /// <summary>Closes the wizard dialog.</summary>
    void Close();

    /// <summary>Enqueues an action to run on the UI thread after the current call stack unwinds.</summary>
    void BeginInvokeOnUI(Action action);

    /// <summary>
    /// Unsubscribes each step from the ReplaceFollowingSteps event handler and disposes it.
    /// Centralises cleanup so extracted handlers don't need the dialog's event handler delegate.
    /// </summary>
    void UnsubscribeAndDispose(IEnumerable<WizardStepPage> steps);

    /// <summary>
    /// Subscribes a step to the ReplaceFollowingSteps event handler managed by the dialog.
    /// </summary>
    void SubscribeStep(WizardStepPage step);
}
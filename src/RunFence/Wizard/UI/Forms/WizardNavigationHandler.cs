using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Handles wizard step navigation: advancing forward, going back, closing with confirmation,
/// and replacing following steps when a branching step changes the path.
/// </summary>
public class WizardNavigationHandler
{
    private IWizardExecutionContext _ctx = null!;

    /// <summary>
    /// Binds the handler to the per-dialog execution context. Must be called before any operations.
    /// </summary>
    public void Initialize(IWizardExecutionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Loads the steps for the selected template when advancing from the picker (step 0).
    /// Returns true with <paramref name="templateSteps"/> populated on success,
    /// or false with <paramref name="errorMessage"/> set on failure.
    /// </summary>
    public bool AdvanceFromPicker(out IReadOnlyList<WizardStepPage> templateSteps, out string? errorMessage)
    {
        errorMessage = null;
        templateSteps = [];

        var pickerTemplate = _ctx.SelectedTemplate;
        if (pickerTemplate == null)
        {
            errorMessage = "No template selected.";
            return false;
        }

        try
        {
            templateSteps = pickerTemplate.CreateSteps();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to load template: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Goes back one step. If on step 1 or the completion step, returns to the template picker
    /// and disposes all template steps.
    /// </summary>
    public void GoBack()
    {
        _ctx.HideError();

        if (_ctx.CurrentStepIndex == 1 || IsOnCompletionStep())
        {
            var toDispose = _ctx.Steps.Skip(1).ToList();
            _ctx.Steps.RemoveRange(1, _ctx.Steps.Count - 1);
            _ctx.SelectedTemplate = null;
            _ctx.SetCompletionButtonsState(showNavigation: true, cancelText: "Cancel");
            _ctx.ShowStep(0);
            _ctx.BeginInvokeOnUI(() => _ctx.UnsubscribeAndDispose(toDispose));
        }
        else
        {
            _ctx.ShowStep(_ctx.CurrentStepIndex - 1);
        }
    }

    /// <summary>
    /// Handles form closing. Returns true to cancel the close operation.
    /// Blocks close while executing; warns when mid-wizard.
    /// </summary>
    public bool HandleClosing()
    {
        if (_ctx.IsExecuting)
            return true; // cancel close

        if (_ctx.CurrentStepIndex >= 1 && !IsOnCompletionStep())
        {
            var result = MessageBox.Show(
                "The wizard is in progress. Are you sure you want to cancel?",
                "Cancel Setup Wizard",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return true; // cancel close
        }

        foreach (var template in _ctx.Templates)
            template.Cleanup();

        return false; // allow close
    }

    /// <summary>
    /// Replaces all steps that follow <paramref name="senderStep"/> with <paramref name="newSteps"/>.
    /// Called when a branching step changes the path (e.g. ContainerOrAccountStep).
    /// </summary>
    public void ReplaceFollowingSteps(WizardStepPage? senderStep, IReadOnlyList<WizardStepPage> newSteps)
    {
        int senderIndex = senderStep != null ? _ctx.Steps.IndexOf(senderStep) : -1;
        if (senderIndex < 0 || senderIndex < _ctx.CurrentStepIndex)
            return;
        if (senderIndex >= _ctx.Steps.Count - 1 && newSteps.Count == 0)
            return;

        var toDispose = _ctx.Steps.Skip(senderIndex + 1).ToList();
        _ctx.Steps.RemoveRange(senderIndex + 1, _ctx.Steps.Count - senderIndex - 1);

        foreach (var ns in newSteps)
            _ctx.SubscribeStep(ns);
        _ctx.Steps.AddRange(newSteps);

        bool isLastTemplateStep = _ctx.SelectedTemplate != null && _ctx.CurrentStepIndex == _ctx.Steps.Count - 1;
        _ctx.SetNextText(isLastTemplateStep ? "Apply" : "Next \u2192");

        _ctx.InvalidateStepIndicator();

        _ctx.UnsubscribeAndDispose(toDispose);
    }

    private bool IsOnCompletionStep() =>
        _ctx.Steps.Count > 1 && _ctx.CurrentStepIndex == _ctx.Steps.Count - 1 &&
        _ctx.Steps[_ctx.CurrentStepIndex] is CompletionStep;
}
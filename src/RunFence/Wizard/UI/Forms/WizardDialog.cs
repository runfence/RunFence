using RunFence.Apps.UI;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// The main wizard dialog. Hosts a step sequence of <see cref="WizardStepPage"/> UserControls,
/// swapping them in and out of the content panel as the user navigates forward and backward.
/// <para>
/// Step 0 is always the <see cref="TemplatePickerStep"/>. When the user selects a template and
/// clicks Next, the template's steps are inserted at index 1+. Back from step 1 returns to the picker
/// and removes all template steps from the list.
/// </para>
/// <para>
/// After a template's <see cref="IWizardTemplate.ExecuteAsync"/> completes, the dialog shows a
/// <see cref="Steps.CompletionStep"/>. From there the user can close the dialog.
/// <see cref="PostWizardActions"/> collects post-wizard delegates from all completed templates;
/// <see cref="WizardLauncher"/> executes them after the dialog closes.
/// </para>
/// </summary>
public partial class WizardDialog : RunFence.UI.Forms.ContextHelpForm, IWizardExecutionContext
{
    // Step state — index 0 is always the template picker
    private readonly List<WizardStepPage> _steps = [];
    private readonly TemplatePickerStep _templatePickerStep;
    private readonly IReadOnlyList<IWizardTemplate> _allTemplates;
    private int _currentStepIndex;
    private IWizardTemplate? _selectedTemplate;
    private WizardStepPage? _canProceedSubscribedStep;
    private readonly bool _skipAvailabilityFilter;
    private bool _isTemplateWarmupInProgress;

    // Post-wizard actions accumulated from completed templates
    private readonly List<Action<IWin32Window>> _postWizardActions = [];

    private readonly WizardExecutionHandler _executionHandler;
    private readonly WizardNavigationHandler _navigationHandler;

    /// <summary>
    /// Post-wizard actions queued by completed templates during this session.
    /// <see cref="WizardLauncher"/> executes these after <see cref="Form.ShowDialog()"/> returns.
    /// </summary>
    public IReadOnlyList<Action<IWin32Window>> PostWizardActions => _postWizardActions;

    /// <summary>Number of templates that completed execution during this wizard session.</summary>
    public int TemplateCompletedCount { get; private set; }

    public WizardDialog(
        IEnumerable<IWizardTemplate> templates,
        WizardExecutionHandler executionHandler,
        WizardNavigationHandler navigationHandler)
    {
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        WizardStylingHelper.ApplyModernStyling(
            this, _headerPanel, _contentPanel, _footerPanel, _progressPanel,
            _titleLabel, _statusLabel, _errorLabel);
        _footerPanel.Paint += OnFooterPanelPaint;

        _executionHandler = executionHandler;
        _navigationHandler = navigationHandler;
        _executionHandler.Initialize(this);
        _navigationHandler.Initialize(this);

        _allTemplates = templates.ToList();
        _skipAvailabilityFilter = ModifierKeys.HasFlag(Keys.Shift);
        var initialTemplates = _skipAvailabilityFilter
            ? BuildVisibleTemplates()
            : [];
        _templatePickerStep = new TemplatePickerStep(initialTemplates, isLoading: true);
        _templatePickerStep.DoubleClickedToAdvance += (_, _) => OnNextClick(this, EventArgs.Empty);
        _steps.Add(_templatePickerStep);

        ShowStep(0);
        _ = LoadTemplatesAsync();
    }

    // -----------------------------------------------------------------------------------------
    // IWizardExecutionContext implementation
    // -----------------------------------------------------------------------------------------

    int IWizardExecutionContext.CurrentStepIndex => _currentStepIndex;
    List<WizardStepPage> IWizardExecutionContext.Steps => _steps;

    IWizardTemplate? IWizardExecutionContext.SelectedTemplate
    {
        get => _selectedTemplate;
        set => _selectedTemplate = value;
    }

    IReadOnlyList<IWizardTemplate> IWizardExecutionContext.Templates => _allTemplates;
    bool IWizardExecutionContext.IsExecuting { get; set; }

    List<Action<IWin32Window>> IWizardExecutionContext.PostWizardActions => _postWizardActions;

    int IWizardExecutionContext.TemplateCompletedCount
    {
        get => TemplateCompletedCount;
        set => TemplateCompletedCount = value;
    }

    void IWizardExecutionContext.ShowStep(int index) => ShowStep(index);

    void IWizardExecutionContext.ShowError(string message)
    {
        if (InvokeRequired)
            Invoke(() => ShowError(message));
        else
            ShowError(message);
    }

    void IWizardExecutionContext.HideError() => HideError();
    void IWizardExecutionContext.SetProgressVisible(bool visible) => SetProgressVisible(visible);
    void IWizardExecutionContext.SetNavigationEnabled(bool enabled) => SetNavigationEnabled(enabled);
    void IWizardExecutionContext.SetNextEnabled(bool enabled) => _nextButton.Enabled = enabled;
    void IWizardExecutionContext.SetCancelEnabled(bool enabled) => _cancelButton.Enabled = enabled;
    void IWizardExecutionContext.SetCancelText(string text) => _cancelButton.Text = text;

    void IWizardExecutionContext.SetStatusText(string text)
    {
        if (InvokeRequired)
            Invoke(() => _statusLabel.Text = text);
        else
            _statusLabel.Text = text;
    }

    void IWizardExecutionContext.SetCompletionButtonsState(bool showNavigation, string cancelText)
    {
        _nextButton.Enabled = showNavigation;
        _cancelButton.Text = cancelText;
    }

    void IWizardExecutionContext.SetBackEnabled(bool enabled) => _backButton.Enabled = enabled;
    void IWizardExecutionContext.SetTitleText(string text) => _titleLabel.Text = text;
    void IWizardExecutionContext.SetNextText(string text) => _nextButton.Text = text;
    void IWizardExecutionContext.InvalidateStepIndicator() => _stepIndicatorPanel.Invalidate();
    void IWizardExecutionContext.BeginInvokeOnUI(Action action) => BeginInvoke(action);

    void IWizardExecutionContext.UnsubscribeAndDispose(IEnumerable<WizardStepPage> steps)
    {
        foreach (var step in steps)
        {
            step.ReplaceFollowingSteps -= OnReplaceFollowingSteps;
            step.Dispose();
        }
    }

    void IWizardExecutionContext.SubscribeStep(WizardStepPage step)
        => step.ReplaceFollowingSteps += OnReplaceFollowingSteps;

    // -----------------------------------------------------------------------------------------
    // Step display
    // -----------------------------------------------------------------------------------------

    private void ShowStep(int index)
    {
        if (_canProceedSubscribedStep != null)
        {
            _canProceedSubscribedStep.CanProceedChanged -= OnCanProceedChanged;
            _canProceedSubscribedStep = null;
        }

        _currentStepIndex = index;
        var step = _steps[index];

        _contentPanel.Controls.Clear();
        step.Dock = DockStyle.Fill;
        _contentPanel.Controls.Add(step);
        step.OnActivated();

        _titleLabel.Text = step.StepTitle;
        _stepIndicatorPanel.Invalidate();
        _backButton.Enabled = index > 0 && !_isTemplateWarmupInProgress;
        _nextButton.Enabled = !_isTemplateWarmupInProgress && step.CanProceed;

        bool isLastTemplateStep = _selectedTemplate != null && index == _steps.Count - 1;
        _nextButton.Text = isLastTemplateStep ? "Apply" : "Next \u2192";

        _canProceedSubscribedStep = step;
        step.CanProceedChanged += OnCanProceedChanged;

        HideError();
    }

    private void OnCanProceedChanged(object? sender, EventArgs e)
        => _nextButton.Enabled = !_isTemplateWarmupInProgress && _steps[_currentStepIndex].CanProceed;

    // -----------------------------------------------------------------------------------------
    // Event handlers (thin wrappers — delegate to handlers)
    // -----------------------------------------------------------------------------------------

    private async void OnNextClick(object sender, EventArgs e)
    {
        if (_isTemplateWarmupInProgress)
            return;

        var step = _steps[_currentStepIndex];

        if (step is Steps.CompletionStep)
        {
            Close();
            return;
        }

        var error = step.Validate();
        if (error != null)
        {
            ShowError(error);
            return;
        }

        HideError();
        step.Collect();

        if (!await _executionHandler.CommitStepAsync(step))
            return;

        bool isLastTemplateStep = _selectedTemplate != null && _currentStepIndex == _steps.Count - 1;

        if (_currentStepIndex == 0)
        {
            _selectedTemplate = _templatePickerStep.SelectedTemplate!;
            if (!_navigationHandler.AdvanceFromPicker(out var templateSteps, out var loadError))
            {
                _selectedTemplate = null;
                ShowError(loadError!);
                return;
            }

            foreach (var ts in templateSteps)
                ts.ReplaceFollowingSteps += OnReplaceFollowingSteps;
            _steps.AddRange(templateSteps);

            if (_steps.Count > 1)
                ShowStep(1);
            else
                await _executionHandler.ExecuteTemplateAsync();
        }
        else if (isLastTemplateStep)
        {
            await _executionHandler.ExecuteTemplateAsync();
        }
        else
        {
            ShowStep(_currentStepIndex + 1);
        }
    }

    private void OnBackClick(object sender, EventArgs e)
        => _navigationHandler.GoBack();

    private void OnCancelClick(object sender, EventArgs e)
    {
        if (((IWizardExecutionContext)this).IsExecuting)
        {
            _executionHandler.CancelExecution();
            return;
        }

        Close();
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
        => e.Cancel = ((IWizardExecutionContext)this).IsExecuting || _navigationHandler.HandleClosing();

    private void OnReplaceFollowingSteps(object? sender, IReadOnlyList<WizardStepPage> newSteps)
        => _navigationHandler.ReplaceFollowingSteps(sender as WizardStepPage, newSteps);

    // -----------------------------------------------------------------------------------------
    // Progress / error UI helpers
    // -----------------------------------------------------------------------------------------

    private void SetProgressVisible(bool visible)
    {
        _progressPanel.Visible = visible;
        _contentPanel.Visible = !visible;
        if (visible)
            _statusLabel.Text = "Please wait...";
    }

    private void SetNavigationEnabled(bool enabled)
    {
        _backButton.Enabled = enabled;
        _nextButton.Enabled = enabled;
        _cancelButton.Enabled = enabled;
    }

    private void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }

    private void HideError()
    {
        _errorLabel.Visible = false;
        _errorLabel.Text = string.Empty;
    }

    // -----------------------------------------------------------------------------------------
    // Painting
    // -----------------------------------------------------------------------------------------

    private void OnStepIndicatorPaint(object? sender, PaintEventArgs e)
        => WizardStylingHelper.PaintStepIndicator(e.Graphics, (Panel)sender!, _steps.Count, _currentStepIndex);

    private void OnFooterPanelPaint(object? sender, PaintEventArgs e)
        => WizardStylingHelper.PaintFooterSeparator(e.Graphics, ((Panel)sender!).Width);

    private async Task LoadTemplatesAsync()
    {
        SetTemplateWarmupState(true);

        try
        {
            await Task.WhenAll(_allTemplates.Select(t => t.WarmCacheAsync()));

            if (IsDisposed || Disposing)
                return;

            if (!_skipAvailabilityFilter)
                _templatePickerStep.SetTemplates(BuildVisibleTemplates());
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing)
                return;

            if (!_skipAvailabilityFilter)
                _templatePickerStep.SetTemplates([]);
            ShowError($"Failed to load templates: {ex.Message}");
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _templatePickerStep.SetLoading(false);
                SetTemplateWarmupState(false);
            }
        }
    }

    private List<IWizardTemplate> BuildVisibleTemplates() =>
        _allTemplates
            .Where(t => _skipAvailabilityFilter || t.IsAvailable)
            .OrderByDescending(t => t.IsPrerequisite)
            .ToList();

    private void SetTemplateWarmupState(bool isLoading)
    {
        _isTemplateWarmupInProgress = isLoading;
        _templatePickerStep.SetLoading(isLoading);
        UseWaitCursor = isLoading;

        if (_steps.Count == 0 || _currentStepIndex != 0)
            return;

        _backButton.Enabled = false;
        _nextButton.Enabled = !isLoading && _steps[_currentStepIndex].CanProceed;
    }
}

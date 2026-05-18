using RunFence.Apps.UI;
using RunFence.UI.Forms;

namespace RunFence.SidMigration.UI.Forms;

public partial class SidMigrationDialog : ContextHelpForm
{
    private readonly SidMigrationWorkflowController _workflowController;
    private bool _workflowControllerReleased;

    public bool InAppMigrationApplied => _workflowController.InAppMigrationApplied;

    public SidMigrationDialog(SidMigrationWorkflowController workflowController)
    {
        _workflowController = workflowController;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        Disposed += OnDialogDisposed;
        _workflowController.ViewChanged += OnWorkflowViewChanged;
        _workflowController.CloseRequested += OnWorkflowCloseRequested;
        _workflowController.Initialize();
    }

    private void OnWorkflowViewChanged()
    {
        var view = _workflowController.CurrentView;
        var stepControl = view.StepView.View;
        _stepTitleLabel.Text = view.Title;
        _nextButton.Text = view.NextText;
        _nextButton.Enabled = view.NextEnabled;
        _nextButton.Visible = view.NextVisible;
        _secondaryButton.Text = view.SecondaryText;
        _secondaryButton.Enabled = view.SecondaryEnabled;
        CancelButton = view.SecondaryActsAsCancel ? _secondaryButton : null;

        if (_stepPanel.Controls.Count != 1 ||
            !ReferenceEquals(_stepPanel.Controls[0], stepControl))
        {
            foreach (Control existingControl in _stepPanel.Controls.Cast<Control>().ToArray())
            {
                _stepPanel.Controls.Remove(existingControl);
                existingControl.Dispose();
            }

            _stepPanel.Controls.Add(stepControl);
        }

        _workflowController.HandleViewShown(this);
    }

    private void OnWorkflowCloseRequested()
    {
        Close();
    }

    private void OnNextClick(object? sender, EventArgs e)
    {
        _workflowController.HandleNext(this);
    }

    private void OnSecondaryButtonClick(object? sender, EventArgs e)
    {
        _workflowController.HandleSecondary(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _workflowController.HandleEscape(this))
            return true;

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _workflowController.HandleFormClosing(this, e);
        if (e.CloseReason == CloseReason.UserClosing && !e.Cancel)
            DialogResult = _workflowController.GetCloseDialogResult();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ReleaseWorkflowController();
        base.OnFormClosed(e);
    }

    private void ReleaseWorkflowController()
    {
        if (_workflowControllerReleased)
            return;

        _workflowController.ViewChanged -= OnWorkflowViewChanged;
        _workflowController.CloseRequested -= OnWorkflowCloseRequested;
        _workflowController.Dispose();
        _workflowControllerReleased = true;
    }

    private void OnDialogDisposed(object? sender, EventArgs e)
    {
        ReleaseWorkflowController();
    }
}

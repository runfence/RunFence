namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Final step shown after a template's <see cref="IWizardTemplate.ExecuteAsync"/> completes.
/// Displays a summary of what was created and any non-fatal errors.
/// Closing is done via the "Done" button (Next) or the "Close" button (Cancel).
/// </summary>
public class CompletionStep : WizardStepPage
{
    private readonly Label _summaryLabel;
    private readonly ListBox _errorListBox;

    public CompletionStep(string summary, IReadOnlyList<string> errors)
    {
        _summaryLabel = new Label
        {
            Text = summary,
            Dock = DockStyle.Top,
            AutoSize = false,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(0x1A, 0x1A, 0x1A),
            Padding = new Padding(0, 8, 0, 8)
        };
        TrackWrappingLabel(_summaryLabel);

        _errorListBox = new ListBox
        {
            Dock = DockStyle.Top,
            Height = 100,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(0xC0, 0x20, 0x20),
            Visible = errors.Count > 0,
            BorderStyle = BorderStyle.FixedSingle,
            SelectionMode = SelectionMode.None
        };
        foreach (var err in errors)
            _errorListBox.Items.Add(err);

        Controls.Add(_errorListBox);
        Controls.Add(_summaryLabel);

        BackColor = Color.White;
    }

    public override string StepTitle => "Done";
    public override string? Validate() => null;

    public override void Collect()
    {
    }
}
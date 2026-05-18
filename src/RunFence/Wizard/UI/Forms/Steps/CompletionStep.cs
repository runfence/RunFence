namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Final step shown after a template's <see cref="IWizardTemplate.ExecuteAsync"/> completes.
/// Displays a summary of what was created and any non-fatal warnings or errors.
/// Closing is done via the "Done" button (Next) or the "Close" button (Cancel).
/// </summary>
public class CompletionStep : WizardStepPage
{
    public CompletionStep(string summary, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
    {
        var summaryLabel = new Label
        {
            Text = summary,
            Dock = DockStyle.Top,
            AutoSize = false,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(0x1A, 0x1A, 0x1A),
            Padding = new Padding(0, 8, 0, 8)
        };
        TrackWrappingLabel(summaryLabel);

        var detailsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = false,
            BackColor = Color.White
        };

        AddDetailSection(
            detailsPanel,
            "Warnings",
            warnings,
            Color.FromArgb(0x8A, 0x5A, 0x00));
        AddDetailSection(
            detailsPanel,
            "Errors",
            errors,
            Color.FromArgb(0xC0, 0x20, 0x20));

        Controls.Add(detailsPanel);
        Controls.Add(summaryLabel);

        BackColor = Color.White;
    }

    public override string StepTitle => "Done";
    public override string? Validate() => null;

    public override void Collect()
    {
    }

    private static void AddDetailSection(TableLayoutPanel panel, string title, IReadOnlyList<string> items, Color textColor)
    {
        if (items.Count == 0)
            return;

        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = textColor,
            Padding = new Padding(0, 8, 0, 4)
        });

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            ForeColor = textColor,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            SelectionMode = SelectionMode.None
        };
        foreach (var item in items)
            listBox.Items.Add(item);

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.Controls.Add(listBox);
    }
}

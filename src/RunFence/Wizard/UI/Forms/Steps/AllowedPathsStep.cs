namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for building a list of folder paths the new account will be allowed to access.
/// Used by the Browser template for allowed download/document folders.
/// </summary>
public class AllowedPathsStep : WizardStepPage
{
    private readonly Action<List<string>> _setPaths;
    private readonly string _labelText;
    private readonly string _stepTitle;

    private FolderListEditor _editor = null!;

    public AllowedPathsStep(
        Action<List<string>> setPaths,
        string? labelText = null,
        string? stepTitle = null)
    {
        _setPaths = setPaths;
        _labelText = labelText ?? "Add folders this account should be able to access:";
        _stepTitle = stepTitle ?? "Allowed Folders";
        BuildContent();
    }

    public override string StepTitle => _stepTitle;

    public override string? Validate() => null;

    public override void Collect()
    {
        _setPaths(_editor.GetItems().ToList());
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _editor = new FolderListEditor();
        _editor.Initialize(_labelText, FolderBrowseDialogType.FolderWithoutCreate);

        Controls.Add(_editor);
        ResumeLayout(false);
    }
}

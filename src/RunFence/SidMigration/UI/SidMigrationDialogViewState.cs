namespace RunFence.SidMigration.UI;

public sealed record SidMigrationDialogViewState(
    ISidMigrationStepView StepView,
    string Title,
    string NextText,
    bool NextEnabled,
    bool NextVisible,
    string SecondaryText,
    bool SecondaryEnabled,
    bool SecondaryActsAsCancel);

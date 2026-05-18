namespace RunFence.SidMigration.UI;

public sealed record SidMigrationSelectionRow(
    int RowIndex,
    string Action,
    string OldSid,
    string NewSidInput,
    string Name);

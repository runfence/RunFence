namespace RunFence.Wizard.UI.Forms.Steps;

public sealed record AppPathBrowseConfiguration(
    string DialogTitle,
    string FileFilter,
    string? InitialPath,
    AppPathBrowseMode BrowseMode);

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Determines which file/folder browse dialog <see cref="FolderListEditor"/> shows when the user
/// clicks Add.
/// </summary>
public enum FolderBrowseDialogType
{
    /// <summary>Standard folder picker. The user may create new folders.</summary>
    FolderWithCreate,

    /// <summary>Standard folder picker. Creating new folders is not allowed.</summary>
    FolderWithoutCreate,

    /// <summary>File open dialog filtered to executable files (*.exe).</summary>
    ExecutableFile,
}

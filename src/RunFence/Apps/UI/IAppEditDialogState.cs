using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Reads dialog control state for <see cref="AppEditDialogController"/>.
/// </summary>
public interface IAppEditDialogState
{
    string NameText { get; }
    string FilePathText { get; }
    bool IsFolder { get; }
    object? SelectedAccountItem { get; }
    bool ManageShortcuts { get; }
    PrivilegeLevel? SelectedPrivilegeLevel { get; }
    bool OverrideIpcCallers { get; }
    string DefaultArgsText { get; }
    bool AllowPassArgs { get; }
    string WorkingDirText { get; }
    bool AllowPassWorkDir { get; }
    string StatusText { set; }
    string? ArgumentsTemplateText { get; }
}
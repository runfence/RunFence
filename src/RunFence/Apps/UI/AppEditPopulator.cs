using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Loads existing <see cref="AppEntry"/> data into an <see cref="AppEditState"/> record
/// for application by <see cref="AppEditDialog"/> to its controls.
/// Separates data reading from UI control manipulation.
/// </summary>
public class AppEditPopulator
{
    /// <summary>
     /// Reads all relevant fields from <paramref name="app"/> and returns a populated state record.
     /// The dialog applies the returned state to its controls.
     /// </summary>
    public AppEditState LoadExistingApp(AppEntry app)
    {
        return new AppEditState(
            Name: app.Name,
            ExePath: app.ExePath,
            IsFolder: app.IsFolder,
            DefaultArguments: app.DefaultArguments,
            AllowPassingArguments: app.AllowPassingArguments,
            ArgumentsTemplate: app.ArgumentsTemplate ?? "",
            WorkingDirectory: app.WorkingDirectory ?? "",
            AllowPassingWorkingDirectory: app.AllowPassingWorkingDirectory,
            ManageShortcuts: app.ManageShortcuts,
            SelectedPrivilegeLevel: app.PrivilegeLevel,
            OverrideIpcCallers: app.AllowedIpcCallers != null);
    }
}

/// <summary>
/// Holds pre-computed display state for an existing app entry, as read by <see cref="AppEditPopulator"/>.
/// </summary>
public record AppEditState(
    string Name,
    string ExePath,
    bool IsFolder,
    string DefaultArguments,
    bool AllowPassingArguments,
    string ArgumentsTemplate,
    string WorkingDirectory,
    bool AllowPassingWorkingDirectory,
    bool ManageShortcuts,
    PrivilegeLevel? SelectedPrivilegeLevel,
    bool OverrideIpcCallers);

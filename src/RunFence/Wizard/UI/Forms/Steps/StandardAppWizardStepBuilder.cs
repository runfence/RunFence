using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Infrastructure;
using RunFence.Launching.Resolution;

namespace RunFence.Wizard.UI.Forms.Steps;

public class StandardAppWizardStepBuilder(
    IShortcutDiscoveryService discoveryService,
    IShortcutIconHelper iconHelper,
    IOpenFileDialogAdapterFactory openFileDialogFactory,
    IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory,
    IAppDiscoveryDialogService appDiscoveryDialogService,
    IExecutablePathResolver executablePathResolver)
{
    public WizardStepPage CreateAppPathStep(
        Action<string, string> setPathAndName,
        string? description,
        string? initialPath = null,
        string? initialName = null)
        => new AppPathStep(
            setPathAndName,
            discoveryService,
            iconHelper,
            openFileDialogFactory,
            folderBrowserDialogFactory,
            appDiscoveryDialogService,
            executablePathResolver,
            description,
            initialPath,
            initialName);

    public AllowedPathsStep CreateAllowedFoldersStep(
        Action<List<string>> setPaths,
        string labelText,
        string stepTitle = "Allowed Folders")
        => new(setPaths, openFileDialogFactory, labelText, stepTitle);

    public AllowedPathsStep CreateProjectFoldersStep(Action<List<string>> setPaths)
        => CreateAllowedFoldersStep(
            setPaths,
            "Add project folders this account should be able to access:",
            "Project Folders");
}

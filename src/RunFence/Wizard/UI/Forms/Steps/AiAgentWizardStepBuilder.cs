using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Wizard.UI.Forms.Steps;

public class AiAgentWizardStepBuilder(
    WizardAccountSetupHelperFactory setupHelperFactory,
    IShortcutDiscoveryService discoveryService,
    IShortcutIconHelper iconHelper,
    IOpenFileDialogAdapterFactory openFileDialogFactory,
    IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory,
    IAppDiscoveryDialogService appDiscoveryDialogService)
{
    public WizardStepPage CreateAccountNameStep(
        Action<string, ProtectedString> setNameAndPassword,
        string description)
        => setupHelperFactory.CreateAccountNameStep(
            setNameAndPassword,
            description: description);

    public AllowedPathsStep CreateProjectFoldersStep(Action<List<string>> setPaths) =>
        new(
            setPaths,
            openFileDialogFactory,
            "Add project folders this account should be able to access:",
            "Project Folders");

    public FirewallOptionsStep CreateFirewallOptionsStep(
        Action<bool, bool, bool> setOptions,
        bool defaultInternet,
        bool defaultLan,
        bool defaultLocalhost)
        => new(
            setOptions,
            defaultInternet,
            defaultLan,
            defaultLocalhost);

    public AiAgentToolStep CreateToolStep(
        Action<bool, string?> setOptions,
        Func<IWizardProgressReporter, Task>? commitAction)
        => new(
            setOptions,
            discoveryService,
            iconHelper,
            openFileDialogFactory,
            folderBrowserDialogFactory,
            appDiscoveryDialogService,
            commitAction);
}

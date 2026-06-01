using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Wizard.UI.Forms.Steps;

public class GamingWizardStepBuilder(
    WizardAccountSetupHelperFactory setupHelperFactory,
    IShortcutDiscoveryService discoveryService,
    IShortcutIconHelper iconHelper,
    IOpenFileDialogAdapterFactory openFileDialogFactory,
    IAppDiscoveryDialogService appDiscoveryDialogService)
{
    public GamingSetupInstructionsStep CreateInstructionsStep(bool isCreateNew) =>
        new(isCreateNew);

    public WizardStepPage CreateAccountNameStep(
        Action<string, ProtectedString> setNameAndPassword,
        bool showPassword,
        bool requirePassword,
        string description)
        => setupHelperFactory.CreateAccountNameStep(
            setNameAndPassword,
            showPassword: showPassword,
            description: description,
            requirePassword: requirePassword);

    public GamingFoldersStep CreateFoldersStep(Action<List<string>> setPaths) =>
        new(setPaths, openFileDialogFactory);

    public GamingLaunchersStep CreateLaunchersStep(
        Action<List<string>> setLauncherPaths,
        Func<string?>? getSid)
        => new(
            setLauncherPaths,
            discoveryService,
            appDiscoveryDialogService,
            iconHelper,
            openFileDialogFactory,
            getSid);
}

using RunFence.Account.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI;

/// <summary>
/// Owns AppEditDialog runtime initialization and binding for new/existing app flows.
/// </summary>
public class AppEditDialogInitializationBinder(
    AppEditDialogPopulator populator,
    AppEditDialogInitializer initializer,
    CredentialDisplayItemFactory credentialDisplayItemFactory,
    Func<IpcCallerSection> ipcCallerSectionFactory)
{
    public void BuildDynamicContent(AppEditDialog dialog, AppEditDialogOptions options)
    {
        var accountItems = populator.BuildAccountItems(
            dialog.Credentials.ToList(),
            dialog.SidNames,
            dialog.ExistingApp,
            dialog.DatabaseOrNull,
            options.AccountSid);
        var configItems = populator.BuildConfigItems();
        dialog.BuildDynamicContentCore(options, accountItems, configItems, ipcCallerSectionFactory());
    }

    public void BindNewApp(AppEditDialog dialog, AppEditDialogOptions options)
    {
        var preGeneratedId = initializer.PreGenerateId(dialog.ExistingApps.Select(a => a.Id));
        var associations = initializer.GetAssociations(dialog.DatabaseOrNull, preGeneratedId);
        dialog.SetPreGeneratedId(preGeneratedId);

        if (options.ConfigPath != null)
            dialog.SelectConfigPath(options.ConfigPath);

        if (options.ExePath != null)
            dialog.SetExePathAndDefaultName(options.ExePath);

        dialog.ApplyNewOptions(options);

        if (options.AccountSid != null)
            dialog.SelectAccountBySid(options.AccountSid);
        else if (options.ContainerName != null)
            dialog.SelectAccountByContainerName(options.ContainerName);

        dialog.SetAssociations(associations);
    }

    public void BindExistingApp(AppEditDialog dialog)
    {
        var existingApp = dialog.ExistingApp;
        if (existingApp == null)
            return;

        var model = initializer.CreateExistingInitializationModel(existingApp, dialog.DatabaseOrNull);
        dialog.ApplyExistingInitializationCore(model);

        var selected = dialog.SelectAccountComboForExisting(model.AccountSelection);
        if (!selected && model.AccountSelection.AppContainerName == null)
        {
            var fallbackEntry = new CredentialEntry { Sid = model.AccountSelection.AccountSid };
            var fallbackItem = credentialDisplayItemFactory.Create(fallbackEntry, dialog.SidNames);
            dialog.AddAccountItemAndSelect(fallbackItem);
        }

        dialog.SelectConfigAndAclPath(model);
    }
}

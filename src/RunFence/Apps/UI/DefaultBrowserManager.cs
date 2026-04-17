using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Manages setting and unsetting the default browser registration for an <see cref="AppEntry"/>.
/// Owns the browser-specific subset of deps extracted from <see cref="AppContextMenuOrchestrator"/>.
/// </summary>
public class DefaultBrowserManager(
    IDatabaseProvider databaseProvider,
    IHandlerMappingService handlerMappingService,
    IAppHandlerRegistrationService handlerRegistrationService,
    ShellHelper shellHelper,
    ILoggingService log)
{
    /// <summary>
    /// Toggles the default browser registration for <paramref name="app"/>:
    /// removes all four browser handler keys if the app is already the default, or registers
    /// them (and opens Default Apps settings) if it is not.
    /// </summary>
    public void SetDefaultBrowser(AppEntry app)
    {
        var database = databaseProvider.GetDatabase();
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);

        if (AppEntryHelper.IsDefaultBrowser(app.Id, allMappings))
        {
            foreach (var key in AppEntryHelper.BrowserKeys)
                handlerMappingService.RemoveHandlerMapping(key, app.Id, database);
            var updatedEffective = handlerMappingService.GetEffectiveHandlerMappings(database);
            handlerRegistrationService.Sync(updatedEffective, database.Apps);
        }
        else
        {
            if (!app.AllowPassingArguments)
                app.AllowPassingArguments = true;

            foreach (var key in AppEntryHelper.BrowserKeys)
                handlerMappingService.SetHandlerMapping(key, new HandlerMappingEntry(app.Id, "\"%1\""), database);

            var updatedEffective = handlerMappingService.GetEffectiveHandlerMappings(database);
            // AutoSetForAllUsers is intentionally NOT called here. Browser keys (http, https, .htm, .html)
            // are DefaultAppsOnly — Windows ignores HKCU overrides for these protocols/extensions.
            // Only HKLM registration via Sync() is meaningful. HKCU auto-set is filtered out by
            // AssociationAutoSetService.GetEffectiveAutoSetMappings for all DefaultAppsOnly keys.
            handlerRegistrationService.Sync(updatedEffective, database.Apps);

            MessageBox.Show(
                $"Registered as \"{Constants.HandlerRegisteredAppName}\".\n\n" +
                "The Default Apps settings will now open. Find \"RunFence\" in the browser list and set it as default.",
                "Default Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            try
            {
                shellHelper.OpenDefaultAppsSettings();
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to open Default Apps settings: {ex.Message}");
            }
        }
    }
}

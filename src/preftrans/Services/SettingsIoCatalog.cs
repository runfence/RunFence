using PrefTrans.Services.IO;

namespace PrefTrans.Services;

public static class SettingsIoCatalog
{
    public static ISettingsIO[] CreateAll(
        ISafeExecutor safe,
        IBroadcastHelper broadcast,
        IUserProfileFilter userProfileFilter)
    {
        var taskbarProfilePathPatcher = new TaskbarProfilePathPatcher();
        var taskbarLegacyOwnershipDetector = new TaskbarLegacyOwnershipDetector(taskbarProfilePathPatcher);
        ITaskbarRegistryStore taskbarRegistryStore = new TaskbarRegistryStore(safe);
        var pinnedShortcutFolderProvider = new DefaultPinnedShortcutFolderProvider();
        var pinnedShortcutReader = new WshPinnedShortcutReader();
        var pinnedShortcutFileStore = new FileSystemPinnedShortcutFileStore();
        IPinnedShortcutTransferService pinnedShortcutTransferService = new PinnedShortcutTransferService(
            safe,
            userProfileFilter,
            taskbarProfilePathPatcher,
            pinnedShortcutFolderProvider,
            pinnedShortcutReader,
            pinnedShortcutFileStore);
        var mouse = new MouseSettingsIO(safe, broadcast);
        var keyboard = new KeyboardSettingsIO(safe, broadcast);
        var scroll = new ScrollSettingsIO(safe, broadcast);
        var explorer = new ExplorerSettingsIO(safe, broadcast);
        var desktop = new DesktopSettingsIO(safe, broadcast);
        var taskbar = new TaskbarSettingsIO(
            taskbarRegistryStore,
            pinnedShortcutTransferService,
            taskbarLegacyOwnershipDetector,
            taskbarProfilePathPatcher,
            broadcast);
        var theme = new ThemeSettingsIO(safe, broadcast);
        var screenSaver = new ScreenSaverSettingsIO(safe, broadcast);
        var inputLanguage = new InputLanguageSettingsIO(safe, broadcast);
        var accessibility = new AccessibilitySettingsIO(safe, broadcast);
        var regional = new RegionalSettingsIO(safe, broadcast);
        var trayIcons = new TrayIconsSettingsIO(safe, broadcast);
        var notifications = new NotificationsSettingsIO(safe, broadcast);
        var userFolders = new UserFoldersSettingsIO(safe, broadcast, userProfileFilter);
        var fileAssociations = new FileAssociationsSettingsIO(safe);
        var nightLight = new NightLightSettingsIO(safe);

        return
        [
            mouse,
            keyboard,
            scroll,
            explorer,
            desktop,
            taskbar,
            theme,
            screenSaver,
            inputLanguage,
            accessibility,
            regional,
            trayIcons,
            notifications,
            userFolders,
            fileAssociations,
            nightLight
        ];
    }
}

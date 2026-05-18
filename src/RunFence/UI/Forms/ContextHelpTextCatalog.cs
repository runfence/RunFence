using System.Globalization;
using System.Resources;

namespace RunFence.UI.Forms;

internal static class ContextHelpTextCatalog
{
    private static readonly ResourceManager ResourceManager = new(
        "RunFence.UI.Forms.ContextHelpTextCatalog",
        typeof(ContextHelpTextCatalog).Assembly);

    public static string AppEdit_ExtraConfigStore => Get(nameof(AppEdit_ExtraConfigStore));
    public static string Launch_PrivilegeLevel => Get(nameof(Launch_PrivilegeLevel));
    public static string Launch_Arguments => Get(nameof(Launch_Arguments));
    public static string Launcher_LauncherAccessGlobal => Get(nameof(Launcher_LauncherAccessGlobal));
    public static string AppEdit_LauncherAccessOverride => Get(nameof(AppEdit_LauncherAccessOverride));
    public static string AppEdit_Acl_ModeDeny => Get(nameof(AppEdit_Acl_ModeDeny));
    public static string AppEdit_Acl_ModeAllow => Get(nameof(AppEdit_Acl_ModeAllow));
    public static string AppEdit_Acl_DeniedRights => Get(nameof(AppEdit_Acl_DeniedRights));
    public static string AclManager_Grants => Get(nameof(AclManager_Grants));
    public static string AclManager_Traverse => Get(nameof(AclManager_Traverse));
    public static string Firewall_ScopeLoopbackFilter => Get(nameof(Firewall_ScopeLoopbackFilter));
    public static string Firewall_InternetAllowlist => Get(nameof(Firewall_InternetAllowlist));
    public static string Firewall_LocalhostAllowlist => Get(nameof(Firewall_LocalhostAllowlist));
    public static string App_PathPrefixes => Get(nameof(App_PathPrefixes));
    public static string App_HandlerMappings => Get(nameof(App_HandlerMappings));
    public static string ExtraConfig_InlineSummary => Get(nameof(ExtraConfig_InlineSummary));
    public static string Options_DragBridge => Get(nameof(Options_DragBridge));
    public static string Options_FolderBrowser => Get(nameof(Options_FolderBrowser));
    public static string Options_DesktopSettingsTransfer => Get(nameof(Options_DesktopSettingsTransfer));
    public static string Options_FirewallIcmp => Get(nameof(Options_FirewallIcmp));
    public static string Account_LogonRestriction => Get(nameof(Account_LogonRestriction));
    public static string Account_NetworkLoginRestriction => Get(nameof(Account_NetworkLoginRestriction));
    public static string Account_BgAutorunRestriction => Get(nameof(Account_BgAutorunRestriction));
    public static string Account_Groups => Get(nameof(Account_Groups));
    public static string Account_AppContainersAclManager => Get(nameof(Account_AppContainersAclManager));
    public static string EphemeralIdentity => Get(nameof(EphemeralIdentity));
    public static string Account_DesktopSettingsImport => Get(nameof(Account_DesktopSettingsImport));
    public static string AppContainer_ComAccess => Get(nameof(AppContainer_ComAccess));
    public static string RunAs_Shortcut => Get(nameof(RunAs_Shortcut));
    public static string App_EnvironmentVariables => Get(nameof(App_EnvironmentVariables));

    private static string Get(string name)
        => ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
           ?? throw new InvalidOperationException($"Missing contextual help text '{name}'.");
}

using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class AppSettings
{
    public bool AutoStartOnLogin { get; set; }
    public int IdleTimeoutMinutes { get; set; }
    public bool AutoLockOnMinimize { get; set; }
    public int AutoLockTimeoutMinutes { get; set; }
    public string FolderBrowserExePath { get; set; } = Constants.FolderBrowserExeName;
    public string FolderBrowserArguments { get; set; } = "\"%1\"";
    public string DefaultDesktopSettingsPath { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnlockMode UnlockMode { get; set; } = UnlockMode.Admin;

    public bool EnableRunAsContextMenu { get; set; }
    public bool EnableLogging { get; set; } = true;
    public string LastSecurityFindingsHash { get; set; } = "";

    /// <summary>
    /// Keys of disk root ACL findings that have been seen at least once.
    /// Once a key is recorded here it is never removed — reappearing findings are silently suppressed.
    /// New findings (not in this set) still trigger the startup security dialog.
    /// </summary>
    public List<string> SeenDiskRootAclKeys { get; set; } = [];

    public bool HasShownFirstAccountWarning { get; set; }
    public bool UacSameAccountWarningSuppressed { get; set; }

    // Last used RunAs selection (persisted for pre-selection in RunAs dialog; mutually exclusive)
    public string? LastUsedRunAsAccountSid { get; set; }
    public string? LastUsedRunAsContainerName { get; set; }

    /// <summary>
    /// Handler associations for main-config apps: extension/protocol → appId.
    /// Keys: file extensions start with '.' (e.g., ".pdf"), URL protocols are bare words (e.g., "http").
    /// Null when empty (omitted from JSON).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? HandlerMappings { get; set; }

    /// <summary>
    /// Original value of <c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\CredUI\EnumerateAdministrators</c>
    /// captured before the Quick Elevation wizard first set it to 0.
    /// null = wizard hasn't modified this registry value yet.
    /// -1 = the key/value was absent (Windows default — admins are enumerated).
    /// 0 or 1 = the original DWORD value that was present before the wizard ran.
    /// Used to restore the original state when the Quick Elevation account is deleted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OriginalUacAdminEnumeration { get; set; }

    /// <summary>
    /// SID of the account that caused <see cref="OriginalUacAdminEnumeration"/> to be recorded.
    /// Used to restrict UAC revert to only when that specific account is deleted.
    /// null = legacy config (revert unconditionally to preserve backward compatibility).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UacAdminEnumerationSid { get; set; }

    // Drag Bridge
    public bool EnableDragBridge { get; set; }

    // Keys.Control | Keys.Alt | Keys.Q  (0x20000 | 0x40000 | 0x51 = 0x60051)
    public int DragBridgeCopyHotkey { get; set; } = 0x60051;

    /// <summary>
    /// Returns a shallow copy of this settings object. Primitive and string properties
    /// are value-copied; <see cref="SeenDiskRootAclKeys"/> is list-cloned so the snapshot
    /// and the live database do not share the same list reference.
    /// </summary>
    public AppSettings Clone() => new()
    {
        AutoStartOnLogin = AutoStartOnLogin,
        IdleTimeoutMinutes = IdleTimeoutMinutes,
        AutoLockOnMinimize = AutoLockOnMinimize,
        AutoLockTimeoutMinutes = AutoLockTimeoutMinutes,
        FolderBrowserExePath = FolderBrowserExePath,
        FolderBrowserArguments = FolderBrowserArguments,
        DefaultDesktopSettingsPath = DefaultDesktopSettingsPath,
        UnlockMode = UnlockMode,
        EnableRunAsContextMenu = EnableRunAsContextMenu,
        EnableLogging = EnableLogging,
        LastSecurityFindingsHash = LastSecurityFindingsHash,
        SeenDiskRootAclKeys = SeenDiskRootAclKeys.ToList(),
        HasShownFirstAccountWarning = HasShownFirstAccountWarning,
        UacSameAccountWarningSuppressed = UacSameAccountWarningSuppressed,
        LastUsedRunAsAccountSid = LastUsedRunAsAccountSid,
        LastUsedRunAsContainerName = LastUsedRunAsContainerName,
        EnableDragBridge = EnableDragBridge,
        DragBridgeCopyHotkey = DragBridgeCopyHotkey,
        HandlerMappings = HandlerMappings != null ? new Dictionary<string, string>(HandlerMappings) : null,
        OriginalUacAdminEnumeration = OriginalUacAdminEnumeration,
        UacAdminEnumerationSid = UacAdminEnumerationSid,
    };
}
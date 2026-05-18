using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class AppSettings
{
    public bool AutoStartOnLogin { get; set; }
    public int IdleTimeoutMinutes { get; set; }
    [JsonPropertyName("AutoLockOnMinimize")]
    public bool AutoLockInBackground { get; set; }
    public int AutoLockTimeoutMinutes { get; set; }
    public string FolderBrowserExePath { get; set; } = PathConstants.FolderBrowserExeName;
    public string FolderBrowserArguments { get; set; } = "\"%1\"";
    public string DefaultDesktopSettingsPath { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UnlockMode UnlockMode { get; set; } = UnlockMode.Admin;

    public bool EnableRunAsContextMenu { get; set; }
    public LogVerbosity LogVerbosity { get; set; } = LogVerbosity.Info;
    public string LastSecurityFindingsHash { get; set; } = "";

    /// <summary>
    /// Keys of disk root ACL findings that have been seen at least once.
    /// Once a key is recorded here it is never removed — reappearing findings are silently suppressed.
    /// New findings (not in this set) still trigger the startup security dialog.
    /// </summary>
    public List<string> SeenDiskRootAclKeys { get; set; } = [];

    public bool HasShownFirstAccountWarning { get; set; }
    public bool HasShownUsersGroupWarning { get; set; }
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
    public Dictionary<string, HandlerMappingEntry>? HandlerMappings { get; set; }

    /// <summary>
    /// Direct handler associations: extension/protocol → raw command or HKLM class name.
    /// Stored in main config only — commands/paths are machine-specific.
    /// Null when empty (omitted from JSON).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, DirectHandlerEntry>? DirectHandlerMappings { get; set; }

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

    /// <summary>
    /// SID of the interactive desktop user at last startup. Used to detect interactive user changes
    /// and trigger container SID re-derivation when needed. Empty = never recorded (first run).
    /// </summary>
    public string LastInteractiveUserSid { get; set; } = string.Empty;

    /// <summary>
    /// Once true, the startup evaluation nag remains eligible until license is acquired.
    /// Set when the runtime threshold is met and persisted through the encrypted app database.
    /// </summary>
    public bool NagEligible { get; set; }

    // Drag Bridge
    public bool EnableDragBridge { get; set; }

    // Keys.Control | Keys.Alt | Keys.Q  (0x20000 | 0x40000 | 0x51 = 0x60051)
    public int DragBridgeCopyHotkey { get; set; } = 0x60051;

    // Firewall
    public bool BlockIcmpWhenInternetBlocked { get; set; } = true;

    // Input injection blocking
    public bool BlockInputInjection { get; set; } = true;

    /// <summary>
    /// Returns a settings snapshot. Primitive and string properties are copied, and
    /// mutable list properties are duplicated so callers can mutate the clone without
    /// affecting the source settings.
    /// </summary>
    public AppSettings Clone() => new()
    {
        AutoStartOnLogin = AutoStartOnLogin,
        IdleTimeoutMinutes = IdleTimeoutMinutes,
        AutoLockInBackground = AutoLockInBackground,
        AutoLockTimeoutMinutes = AutoLockTimeoutMinutes,
        FolderBrowserExePath = FolderBrowserExePath,
        FolderBrowserArguments = FolderBrowserArguments,
        DefaultDesktopSettingsPath = DefaultDesktopSettingsPath,
        UnlockMode = UnlockMode,
        EnableRunAsContextMenu = EnableRunAsContextMenu,
        LogVerbosity = LogVerbosity,
        LastSecurityFindingsHash = LastSecurityFindingsHash,
        SeenDiskRootAclKeys = SeenDiskRootAclKeys.ToList(),
        HasShownFirstAccountWarning = HasShownFirstAccountWarning,
        HasShownUsersGroupWarning = HasShownUsersGroupWarning,
        UacSameAccountWarningSuppressed = UacSameAccountWarningSuppressed,
        LastUsedRunAsAccountSid = LastUsedRunAsAccountSid,
        LastUsedRunAsContainerName = LastUsedRunAsContainerName,
        LastInteractiveUserSid = LastInteractiveUserSid,
        NagEligible = NagEligible,
        EnableDragBridge = EnableDragBridge,
        DragBridgeCopyHotkey = DragBridgeCopyHotkey,
        BlockIcmpWhenInternetBlocked = BlockIcmpWhenInternetBlocked,
        BlockInputInjection = BlockInputInjection,
        HandlerMappings = HandlerMappings != null
            ? HandlerMappings.ToDictionary(
                kv => kv.Key,
                kv => kv.Value with
                {
                    PathPrefixes = kv.Value.PathPrefixes?.ToList()
                },
                StringComparer.OrdinalIgnoreCase)
            : null,
        DirectHandlerMappings = DirectHandlerMappings != null
            ? new Dictionary<string, DirectHandlerEntry>(DirectHandlerMappings, StringComparer.OrdinalIgnoreCase)
            : null,
        OriginalUacAdminEnumeration = OriginalUacAdminEnumeration,
        UacAdminEnumerationSid = UacAdminEnumerationSid,
    };
}

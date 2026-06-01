using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class AppEntry
{
    public string Id { get; set; } = GenerateId();
    public string Name { get; set; } = string.Empty;

    public static string GenerateId()
    {
        return string.Create(5, 0, static (span, _) =>
        {
            var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            for (int i = 0; i < span.Length; i++)
                span[i] = chars[Random.Shared.Next(chars.Length)];
        });
    }

    public string ExePath { get; set; } = string.Empty;
    public bool IsUrlScheme { get; set; }
    public bool IsFolder { get; set; }
    public string DefaultArguments { get; set; } = string.Empty;
    public bool AllowPassingArguments { get; set; }
    public bool AllowPassingWorkingDirectory { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrivilegeLevel? PrivilegeLevel { get; set; }

    public string AccountSid { get; set; } = string.Empty;
    public bool RestrictAcl { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AclMode AclMode { get; set; } = AclMode.Deny;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DeniedRights DeniedRights { get; set; } = DeniedRights.Execute;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AllowAclEntry>? AllowedAclEntries { get; set; }

    [JsonConverter(typeof(AclTargetConverter))]
    public AclTarget AclTarget { get; set; } = AclTarget.Folder;

    public int FolderAclDepth { get; set; }
    public bool ManageShortcuts { get; set; } = true;
    public DateTime? LastKnownExeTimestamp { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(SidStringListConverter))]
    public List<string>? AllowedIpcCallers { get; set; }

    /// <summary>
    /// When set, this app runs in the named AppContainer instead of a user account.
    /// Mutually exclusive with AccountSid (AccountSid is empty when this is set).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppContainerName { get; set; }

    /// <summary>
    /// Extra environment variables merged on top of the target user's profile environment at launch time.
    /// Null when empty (omitted from JSON). Not applicable to folder or URL-scheme apps.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Controls how passed arguments interact with this app's args when <see cref="AllowPassingArguments"/> is enabled.
    /// <list type="bullet">
    ///   <item>Null/empty (default): passed args replace <see cref="DefaultArguments"/> entirely (existing behavior).</item>
    ///   <item>Contains <c>%1</c>: passed args (MSVC CRT-safe escaped) replace all <c>%1</c> placeholders.</item>
    ///   <item>No <c>%1</c>: passed args are appended after the template value.</item>
    /// </list>
    /// <see cref="DefaultArguments"/> is still used when no arguments are passed (e.g., direct launch).
    /// Null when not set (omitted from JSON).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArgumentsTemplate { get; set; }

    /// <summary>
    /// Default path prefix constraint for all associations. Null/empty = catch-all.
    /// Per-association overrides add to (union) or replace this list.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? PathPrefixes { get; set; }

    /// <summary>
    /// Retryable app-enforcement failure state (ACL/shortcut/beside-target) recorded when the app
    /// save succeeds but side-effect enforcement fails.
    /// Null when no retry is pending.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppEnforcementRetryStatus? EnforcementRetryStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ShortcutProtectionState>? ShortcutProtectionStates { get; set; }

    public AppEntry Clone() => new()
    {
        Id = Id,
        Name = Name,
        ExePath = ExePath,
        IsUrlScheme = IsUrlScheme,
        IsFolder = IsFolder,
        DefaultArguments = DefaultArguments,
        AllowPassingArguments = AllowPassingArguments,
        AllowPassingWorkingDirectory = AllowPassingWorkingDirectory,
        WorkingDirectory = WorkingDirectory,
        PrivilegeLevel = PrivilegeLevel,
        AccountSid = AccountSid,
        RestrictAcl = RestrictAcl,
        AclMode = AclMode,
        DeniedRights = DeniedRights,
        AllowedAclEntries = AllowedAclEntries?.ToList(),
        AclTarget = AclTarget,
        FolderAclDepth = FolderAclDepth,
        ManageShortcuts = ManageShortcuts,
        LastKnownExeTimestamp = LastKnownExeTimestamp,
        AllowedIpcCallers = AllowedIpcCallers?.ToList(),
        AppContainerName = AppContainerName,
        EnvironmentVariables = EnvironmentVariables != null
            ? new Dictionary<string, string>(EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
            : null,
        ArgumentsTemplate = ArgumentsTemplate,
        PathPrefixes = PathPrefixes?.ToList(),
        EnforcementRetryStatus = EnforcementRetryStatus == null
                ? null
                : new AppEnforcementRetryStatus(
                EnforcementRetryStatus.FailureMessage,
                EnforcementRetryStatus.LastFailedUtc),
        ShortcutProtectionStates = ShortcutProtectionStates?.ToList(),
    };
}

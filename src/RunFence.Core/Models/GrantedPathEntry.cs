using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

/// <summary>
/// Persisted desired rights. Null = not yet saved (legacy entry, auto-populated from NTFS on first open).
/// All fields are stored. Some are always-on per mode (UI locks the checkbox):
///   Allow mode: Read always on. Execute, Write, Special are user-configurable.
///   Deny mode: Write+Special always on. Read, Execute are user-configurable.
/// </summary>
public record SavedRightsState(
    bool Execute,
    bool Write,
    bool Read,
    bool Special,
    bool Own // Allow: account is owner. Deny: admin is owner. Skipped for containers.
)
{
    /// <summary>
    /// Returns the default <see cref="SavedRightsState"/> for a new entry in the given mode.
    /// Allow mode: Read always on, Execute/Write/Special/Own off by default.
    /// Deny mode: Write+Special always on, Execute/Read/Own off by default.
    /// </summary>
    public static SavedRightsState DefaultForMode(bool isDeny, bool own = false) => isDeny
        ? new SavedRightsState(Execute: false, Write: true, Read: false, Special: true, Own: own)
        : new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: own);
}

public class GrantedPathEntry
{
    public string Path { get; set; } = "";

    /// <summary>
    /// When true, this is a traverse-only grant (ExecuteFile+ReadAttributes+Synchronize, no inheritance,
    /// on path + all ancestors up to drive root). Shown in the Traverse tab.
    /// When false, this is a regular allow/deny grant. Shown in the main Grants tab.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTraverseOnly { get; set; }

    /// <summary>
    /// When true, this entry manages Deny ACEs. When false (default), manages Allow ACEs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDeny { get; set; }

    /// <summary>
    /// All directory paths (target + all ancestors to drive root) that had ACEs applied
    /// when this traverse entry was created. Used for reliable cleanup when the folder
    /// structure is moved or renamed. Null for entries created before this feature.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllAppliedPaths { get; set; }

    /// <summary>
    /// Persisted desired rights for this entry. Null = legacy entry (auto-populated from NTFS on first open).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SavedRightsState? SavedRights { get; set; }
}
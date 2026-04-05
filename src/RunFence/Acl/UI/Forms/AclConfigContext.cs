using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Context provided to <see cref="AclConfigSection"/> by its parent dialog.
/// </summary>
public record AclConfigContext(
    IAclConfigContextProvider Provider,
    List<AppEntry> ExistingApps,
    string? CurrentAppId,
    IReadOnlyDictionary<string, string>? SidNames = null);
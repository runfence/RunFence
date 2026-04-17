using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;
using RunFence.RunAs.UI.Forms;

namespace RunFence.RunAs.UI;

/// <summary>
/// Encapsulates the per-use data for <see cref="RunAsDialog.Initialize"/>.
/// </summary>
public record RunAsDialogOptions(
    string FilePath,
    string? Arguments,
    List<CredentialEntry> Credentials,
    List<AppEntry> ExistingApps,
    string? LastUsedAccountSid = null,
    HashSet<string>? SidsNeedingPermission = null,
    IReadOnlyDictionary<string, string>? SidNames = null,
    ShortcutContext? ShortcutContext = null,
    List<AppContainerEntry>? AppContainers = null,
    string? LastUsedContainerName = null,
    string? CurrentUserSid = null,
    IReadOnlyDictionary<string, PrivilegeLevel>? AccountPrivilegeLevels = null);
using RunFence.Acl.UI;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record AppEditInitializationModel(
    AppEditState State,
    AclConfigInitializationModel AclState,
    AppEditExistingAccountSelection AccountSelection,
    string? SelectedConfigPath,
    IReadOnlyList<string>? IpcCallers,
    IReadOnlyDictionary<string, string>? EnvironmentVariables,
    IReadOnlyList<HandlerAssociationItem>? Associations,
    IReadOnlyList<string>? PathPrefixes);

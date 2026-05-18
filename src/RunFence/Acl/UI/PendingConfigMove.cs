using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed record PendingConfigMove(
    GrantedPathEntry Entry,
    string? TargetConfigPath);

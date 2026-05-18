using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public sealed record AppContainerEditSubmitRequest
{
    public AppContainerEntry? Existing { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public bool IsEphemeral { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public bool LoopbackChecked { get; init; }
    public IReadOnlyList<string> ComClsids { get; init; } = [];
}

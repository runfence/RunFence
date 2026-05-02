using RunFence.Core.Models;

namespace RunFence.Launch;

public sealed record AppEntryLaunchPlan(
    AppEntryLaunchKind Kind,
    LaunchIdentity Identity,
    ProcessLaunchTarget? FileTarget = null,
    string? Url = null,
    string? FolderPath = null);

public enum AppEntryLaunchKind
{
    File,
    Url,
    Folder
}

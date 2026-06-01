using RunFence.Apps;
using RunFence.Core.Models;

namespace RunFence.RunAs;

public sealed record RunAsAppEditCommitRequest(
    AppEntry NewApp,
    AppEntry? PreviousApp,
    string? PreviousConfigPath,
    string? ConfigPath,
    AppEntryChangeSet ChangeSet);

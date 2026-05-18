using RunFence.Core.Models;

namespace RunFence.Persistence.UI;

public sealed record AppConfigMismatchLoadResult(
    AppConfig Config,
    bool UsedMismatchKey);

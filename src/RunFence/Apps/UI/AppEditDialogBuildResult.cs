using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record AppEditDialogBuildResult(
    AppEntry? Result,
    string? StatusText = null);

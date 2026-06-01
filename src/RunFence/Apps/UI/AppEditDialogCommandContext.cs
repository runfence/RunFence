namespace RunFence.Apps.UI;

public sealed record AppEditDialogCommandContext(
    Func<AppEditDialogApplyContext, Task> ApplyAsync,
    Func<Task>? RemoveAsync = null);

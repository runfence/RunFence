namespace RunFence.Apps.UI;

public sealed record AppEditDialogCommandContext(
    Func<Task> ApplyAsync,
    Func<Task>? RemoveAsync = null);

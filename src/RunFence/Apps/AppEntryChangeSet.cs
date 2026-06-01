namespace RunFence.Apps;

public readonly record struct AppEntryChangeSet(
    bool RequiresAclReapply,
    bool RequiresBesideTargetRefresh,
    bool RequiresHandlerSync,
    bool RequiresManagedShortcutRefresh,
    bool RequiresIconRefresh,
    AppEditConfigSaveScope ConfigSaveScope);

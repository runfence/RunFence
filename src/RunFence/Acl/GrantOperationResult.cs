namespace RunFence.Acl;

/// <summary>
/// Result returned by grant mutation methods, capturing what was added and whether the
/// in-memory database was modified.
/// </summary>
/// <param name="GrantAdded">True if a new allow/deny ACE was applied on the target path itself.</param>
/// <param name="TraverseAdded">True if new traverse ACEs were applied on ancestor directories.</param>
/// <param name="DatabaseModified">
/// True if the in-memory database was written (main grant, traverse, or interactive user sync).
/// </param>
public readonly record struct GrantOperationResult(bool GrantAdded, bool TraverseAdded, bool DatabaseModified);

namespace RunFence.Account;

/// <summary>
/// Result of <see cref="GroupPolicyScriptHelper.SetLoginBlocked"/> when blocked=true.
/// Contains the script path and any traverse-only ancestor directories that were granted.
/// </summary>
public record SetLoginBlockedResult(string? ScriptPath, List<string>? TraversePaths);

namespace RunFence.Account;

/// <summary>
/// Abstraction for the user-facing prompts shown by <see cref="ProfileRepairHelper"/>
/// during profile corruption detection and repair.
/// </summary>
public interface IProfileRepairPrompt
{
    /// <summary>
    /// Asks the user whether to repair a corrupted profile.
    /// Returns true if the user confirms.
    /// </summary>
    bool ConfirmRepair(string accountName);

    /// <summary>
    /// Notifies the user that profile repair failed.
    /// </summary>
    void NotifyRepairFailed();

    /// <summary>
    /// Asks the user whether to retry the launch after a successful repair.
    /// Returns true if the user wants to retry.
    /// </summary>
    bool ConfirmRetry();
}
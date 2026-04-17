using RunFence.Core;

namespace RunFence.Account;

public interface IProfileRepairHelper
{
    /// <summary>
    /// Wraps a launch action with automatic profile corruption detection and repair
    /// for the specified account SID.
    /// On launch failure: checks if the profile for <paramref name="accountSid"/> was corrupted,
    /// prompts the user to repair, and optionally retries the launch.
    /// If no corruption is detected or <paramref name="accountSid"/> is null,
    /// the original exception is rethrown for normal handling.
    /// </summary>
    void ExecuteWithProfileRepair(Action launchAction, string? accountSid)
        => ExecuteWithProfileRepair<VoidStruct>(() =>
        {
            launchAction();
            return default;
        }, accountSid);

    T ExecuteWithProfileRepair<T>(Func<T> launchAction, string? accountSid);
}
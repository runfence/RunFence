using RunFence.Core;

namespace RunFence.Infrastructure;

public interface IUserImpersonationHelper
{
    /// <summary>
    /// Logs on as the target user, loads their profile, runs the action under impersonation,
    /// then unloads the profile and closes the token.
    /// Returns (profilePath, actionResult). Profile path is resolved after LoadUserProfile
    /// (handles accounts that have never logged on).
    /// </summary>
    (string profilePath, T result) RunImpersonated<T>(
        string targetSid, ProtectedString password, Func<T> action);
}

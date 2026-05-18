namespace RunFence.Infrastructure;

public enum JobAssignment
{
    Tracking,
    Restricted,       // Isolated / medium-IL — Global\RunFence_Job_{SID}_Restricted
    LowIntegrity,     // LowIntegrity — Global\RunFence_Job_{SID}_LowIntegrity
}

public interface IProcessJobManager
{
    /// <summary>
    /// Assigns <paramref name="hProcess"/> to the appropriate job object for <paramref name="sid"/>.
    /// <see cref="JobAssignment.Restricted"/> and <see cref="JobAssignment.LowIntegrity"/> each use
    /// a separate named job with UI restrictions. <see cref="JobAssignment.Tracking"/> uses the
    /// unrestricted tracking job (only when <see cref="SidResolutionHelper.NeedsProcessJobTracking"/>
    /// returns true). Returns explicit failure information when assignment or policy setup fails.
    /// </summary>
    JobAssignmentResult TryAssignToJob(string sid, IntPtr hProcess, JobAssignment assignment, string? jobNameOverride = null);

    /// <summary>
    /// Returns the union of PIDs in all job objects for <paramref name="sid"/>
    /// (tracking, restricted, and low-integrity), or null if none exist yet.
    /// </summary>
    HashSet<int>? GetJobMembers(string sid);

    /// <summary>
    /// Returns the PIDs in the restricted (isLow=false) or low-integrity (isLow=true) job
    /// for <paramref name="sid"/>, or null if the job handle is not cached.
    /// Unlike <see cref="GetJobMembers"/>, this checks only the specific job used by the keeper.
    /// </summary>
    HashSet<int>? GetKeeperJobMembers(string sid, bool isLow);

    /// <summary>
    /// Returns the handle of the restricted or low-integrity job that contains
    /// <paramref name="pid"/>, or <see cref="IntPtr.Zero"/> if not found.
    /// </summary>
    IntPtr TryGetRestrictedJobForPid(int pid);

    /// <summary>
    /// Takes ownership of a verified restricted/low-integrity job handle reopened during
    /// JobKeeper reconnect so membership queries keep working after elevated RunFence restarts.
    /// </summary>
    void RegisterVerifiedRestrictedJob(string sid, bool isLow, IntPtr jobHandle);

    /// <summary>
    /// Closes and discards the cached job handle for <paramref name="sid"/> / <paramref name="assignment"/>.
    /// Must be called before relaunching a job keeper so that the next <see cref="TryAssignToJob"/>
    /// call re-creates the kernel job object from scratch (no UI limits) instead of reopening the
    /// existing one (which already has UI limits, blocking nesting via seclogon).
    /// Existing processes in the old job continue running in it; new processes join the new job.
    /// </summary>
    void ResetJobHandle(string sid, JobAssignment assignment);

}

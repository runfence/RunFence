using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Infrastructure;

/// <summary>
/// Manages per-SID named kernel Job Objects for launched processes.
/// Restricted jobs are fail-closed: callers must not resume a suspended restricted process
/// unless assignment and UI policy application return a successful result.
/// </summary>
public sealed class ProcessJobManager(ILoggingService log, IJobObjectApi jobObjectApi) : IProcessJobManager, IDisposable
{
    public const uint JobObjectKeepAliveAccess = ProcessLaunchNative.SYNCHRONIZE;
    public const uint JobObjectReconnectAccess =
        FileSecurityNative.READ_CONTROL | ProcessLaunchNative.SYNCHRONIZE | JobNative.JOB_OBJECT_QUERY;
    public const string RestrictedJobSecurityDescriptor = "O:BAG:SYD:P(A;;GA;;;SY)(A;;GA;;;BA)";

    private const int ErrorAlreadyExists = 183;

    public const uint UiRestrictionFlags = JobNative.JOB_OBJECT_UILIMIT_HANDLES
        | JobNative.JOB_OBJECT_UILIMIT_DISPLAYSETTINGS
        | JobNative.JOB_OBJECT_UILIMIT_DESKTOP
        | JobNative.JOB_OBJECT_UILIMIT_EXITWINDOWS
        | JobNative.JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS;

    private readonly Dictionary<string, IntPtr> _trackingJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntPtr> _restrictedJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntPtr> _lowIntegrityJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public JobAssignmentResult TryAssignToJob(string sid, IntPtr hProcess, JobAssignment assignment, string? jobNameOverride = null)
    {
        bool needsTracking = SidResolutionHelper.NeedsProcessJobTracking(sid);
        if (!needsTracking && assignment == JobAssignment.Tracking)
            return JobAssignmentResult.Skipped(assignment, "SID does not require tracking job assignment.");

        lock (_lock)
        {
            return assignment switch
            {
                JobAssignment.Restricted => TryAssignToJobImpl(
                    sid, hProcess, jobNameOverride ?? $@"Global\RunFence_Job_{sid}_Restricted",
                    _restrictedJobs, assignment, applyRestrictions: true),
                JobAssignment.LowIntegrity => TryAssignToJobImpl(
                    sid, hProcess, jobNameOverride ?? $@"Global\RunFence_Job_{sid}_LowIntegrity",
                    _lowIntegrityJobs, assignment, applyRestrictions: true),
                _ => TryAssignToJobImpl(
                    sid, hProcess, $@"Global\RunFence_Job_{sid}",
                    _trackingJobs, assignment, applyRestrictions: false),
            };
        }
    }

    public HashSet<int>? GetJobMembers(string sid)
    {
        lock (_lock)
        {
            HashSet<int>? result = null;
            foreach (var jobs in new[] { _trackingJobs, _restrictedJobs, _lowIntegrityJobs })
            {
                if (!jobs.TryGetValue(sid, out var hJob))
                    continue;
                var pids = jobObjectApi.QueryProcessIds(hJob);
                if (pids == null)
                    continue;
                if (result == null)
                    result = pids;
                else
                    result.UnionWith(pids);
            }

            return result;
        }
    }

    public HashSet<int>? GetKeeperJobMembers(string sid, bool isLow)
    {
        lock (_lock)
        {
            var jobs = isLow ? _lowIntegrityJobs : _restrictedJobs;
            return jobs.TryGetValue(sid, out var hJob) ? jobObjectApi.QueryProcessIds(hJob) : null;
        }
    }

    public IntPtr TryGetRestrictedJobForPid(int pid)
    {
        lock (_lock)
        {
            foreach (var jobs in new[] { _restrictedJobs, _lowIntegrityJobs })
            {
                foreach (var (_, hJob) in jobs)
                {
                    var pids = jobObjectApi.QueryProcessIds(hJob);
                    if (pids?.Contains(pid) == true)
                        return hJob;
                }
            }

            return IntPtr.Zero;
        }
    }

    public void RegisterVerifiedRestrictedJob(string sid, bool isLow, IntPtr jobHandle)
    {
        if (jobHandle == IntPtr.Zero)
            throw new ArgumentException("Verified job handle must be nonzero.", nameof(jobHandle));

        lock (_lock)
        {
            var jobs = isLow ? _lowIntegrityJobs : _restrictedJobs;
            if (jobs.TryGetValue(sid, out var existingJob) && existingJob != jobHandle)
                jobObjectApi.CloseHandle(existingJob);

            jobs[sid] = jobHandle;
        }
    }

    private JobAssignmentResult TryAssignToJobImpl(
        string sid,
        IntPtr hProcess,
        string jobName,
        Dictionary<string, IntPtr> jobs,
        JobAssignment assignment,
        bool applyRestrictions)
    {
        int processId = jobObjectApi.GetProcessId(hProcess);
        IntPtr staleHandle = IntPtr.Zero;

        if (jobs.TryGetValue(sid, out var existingJob))
        {
            if (applyRestrictions && !HasExpectedUiRestrictions(existingJob))
            {
                jobs.Remove(sid);
                jobObjectApi.CloseHandle(existingJob);
                return JobAssignmentResult.Failure(assignment, jobName, processId,
                    "Existing restricted job does not have the expected UI restrictions.",
                    JobAssignmentFailureKind.ExistingJobPolicyMismatch);
            }

            if (jobObjectApi.AssignProcessToJobObject(existingJob, hProcess))
            {
                if (applyRestrictions && !HasExpectedUiRestrictions(existingJob))
                {
                    jobs.Remove(sid);
                    jobObjectApi.CloseHandle(existingJob);
                    return JobAssignmentResult.Failure(assignment, jobName, processId,
                        "Existing restricted job lost the expected UI restrictions after assignment.",
                        JobAssignmentFailureKind.ExistingJobPolicyMismatch);
                }

                return JobAssignmentResult.Success(assignment, assignment, jobName, processId,
                    applyRestrictions, limitPolicyApplied: applyRestrictions, existingJob);
            }

            log.Info(
                $"ProcessJobManager: AssignProcessToJobObject failed for existing job {sid}: error {jobObjectApi.GetLastWin32Error()}, reopening handle");
            jobs.Remove(sid);
            staleHandle = existingJob;
        }

        var hJob = jobObjectApi.CreateJobObject(
            jobName,
            applyRestrictions
                ? AdminOperationMockAccessHelper.AppendCurrentProcessGenericAllAce(RestrictedJobSecurityDescriptor)
                : null);
        var createError = jobObjectApi.GetLastWin32Error();

        if (staleHandle != IntPtr.Zero)
            jobObjectApi.CloseHandle(staleHandle);

        if (hJob == IntPtr.Zero)
        {
            var reason = $"Failed to create job object '{jobName}' for {sid}: Win32 error {createError}.";
            log.Warn($"ProcessJobManager: {reason}");
            return JobAssignmentResult.Failure(assignment, jobName, processId, reason, JobAssignmentFailureKind.CreateJobFailed);
        }

        if (applyRestrictions && createError == ErrorAlreadyExists)
        {
            jobObjectApi.CloseHandle(hJob);
            var reason = $"Named restricted job '{jobName}' already exists.";
            log.Warn($"ProcessJobManager: {reason}");
            return JobAssignmentResult.Failure(assignment, jobName, processId, reason, JobAssignmentFailureKind.PreexistingNamedJobRejected);
        }

        if (!jobObjectApi.AssignProcessToJobObject(hJob, hProcess))
        {
            var reason = $"AssignProcessToJobObject failed for {sid}: Win32 error {jobObjectApi.GetLastWin32Error()}.";
            log.Warn($"ProcessJobManager: {reason}");
            jobObjectApi.CloseHandle(hJob);
            return JobAssignmentResult.Failure(assignment, jobName, processId, reason, JobAssignmentFailureKind.AssignProcessFailed);
        }

        if (applyRestrictions && !jobObjectApi.SetUiRestrictions(hJob, UiRestrictionFlags))
        {
            var reason = $"SetUiRestrictions failed for {sid}: Win32 error {jobObjectApi.GetLastWin32Error()}.";
            log.Error($"ProcessJobManager: {reason}");
            jobObjectApi.CloseHandle(hJob);
            return JobAssignmentResult.Failure(assignment, jobName, processId, reason, JobAssignmentFailureKind.UiRestrictionsFailed);
        }

        jobs[sid] = hJob;
        return JobAssignmentResult.Success(assignment, assignment, jobName, processId,
            applyRestrictions, limitPolicyApplied: applyRestrictions, hJob);
    }

    private bool HasExpectedUiRestrictions(IntPtr hJob)
    {
        var flags = jobObjectApi.QueryUiRestrictions(hJob);
        return flags.HasValue && (flags.Value & UiRestrictionFlags) == UiRestrictionFlags;
    }

    public void ResetJobHandle(string sid, JobAssignment assignment)
    {
        lock (_lock)
        {
            var jobs = assignment switch
            {
                JobAssignment.Restricted => _restrictedJobs,
                JobAssignment.LowIntegrity => _lowIntegrityJobs,
                _ => _trackingJobs,
            };
            if (jobs.TryGetValue(sid, out var hJob))
            {
                jobObjectApi.CloseHandle(hJob);
                jobs.Remove(sid);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var jobs in new[] { _trackingJobs, _restrictedJobs, _lowIntegrityJobs })
            {
                foreach (var handle in jobs.Values)
                {
                    if (handle != IntPtr.Zero)
                        jobObjectApi.CloseHandle(handle);
                }
                jobs.Clear();
            }
        }
    }
}

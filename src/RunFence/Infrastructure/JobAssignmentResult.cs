namespace RunFence.Infrastructure;

public sealed record JobAssignmentResult(
    bool Succeeded,
    JobAssignment RequestedKind,
    JobAssignment? AssignedKind,
    string? JobName,
    int AssignedProcessId,
    bool UiRestrictionsApplied,
    bool LimitPolicyApplied,
    string? FailureReason,
    JobAssignmentFailureKind FailureKind,
    IntPtr AssignedJobHandle)
{
    public static JobAssignmentResult Success(
        JobAssignment requestedKind,
        JobAssignment assignedKind,
        string jobName,
        int assignedProcessId,
        bool uiRestrictionsApplied,
        bool limitPolicyApplied,
        IntPtr assignedJobHandle) =>
        new(true, requestedKind, assignedKind, jobName, assignedProcessId, uiRestrictionsApplied,
            limitPolicyApplied, null, JobAssignmentFailureKind.None, assignedJobHandle);

    public static JobAssignmentResult Skipped(JobAssignment requestedKind, string reason) =>
        new(true, requestedKind, null, null, 0, false, false, reason, JobAssignmentFailureKind.None, IntPtr.Zero);

    public static JobAssignmentResult Failure(
        JobAssignment requestedKind,
        string? jobName,
        int assignedProcessId,
        string reason,
        JobAssignmentFailureKind failureKind = JobAssignmentFailureKind.Unknown) =>
        new(false, requestedKind, null, jobName, assignedProcessId, false, false, reason, failureKind, IntPtr.Zero);
}

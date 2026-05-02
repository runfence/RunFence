namespace RunFence.Infrastructure;

public enum JobAssignmentFailureKind
{
    None,
    Unknown,
    CreateJobFailed,
    PreexistingNamedJobRejected,
    AssignProcessFailed,
    UiRestrictionsFailed,
    ExistingJobPolicyMismatch,
}

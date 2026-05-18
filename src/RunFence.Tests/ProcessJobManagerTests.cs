using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using System.Security.Principal;
using Xunit;

namespace RunFence.Tests;

public class ProcessJobManagerTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private static readonly IntPtr ProcessHandle = new(10);
    private static readonly IntPtr JobHandle = new(20);

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IJobObjectApi> _jobApi = new();

    [Fact]
    public void TryAssignToJob_AssignFails_ReturnsFailureBeforeUiPolicy()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        _jobApi.Setup(a => a.CreateJobObject("job", GetExpectedRestrictedJobSecurityDescriptor()))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.GetLastWin32Error()).Returns(0);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, ProcessHandle)).Returns(false);
        _jobApi.Setup(a => a.GetLastWin32Error()).Returns(5);

        var result = manager.TryAssignToJob(Sid, ProcessHandle, JobAssignment.Restricted, "job");

        Assert.False(result.Succeeded);
        Assert.Equal(JobAssignment.Restricted, result.RequestedKind);
        Assert.Equal("job", result.JobName);
        Assert.Equal(JobAssignmentFailureKind.AssignProcessFailed, result.FailureKind);
        Assert.Contains("AssignProcessToJobObject failed", result.FailureReason);
        _jobApi.Verify(a => a.SetUiRestrictions(It.IsAny<IntPtr>(), It.IsAny<uint>()), Times.Never);
    }

    [Fact]
    public void TryAssignToJob_SetUiRestrictionsFails_ReturnsFailure()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        _jobApi.Setup(a => a.CreateJobObject("job", GetExpectedRestrictedJobSecurityDescriptor()))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.GetLastWin32Error()).Returns(0);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, ProcessHandle)).Returns(true);
        _jobApi.Setup(a => a.SetUiRestrictions(JobHandle, ProcessJobManager.UiRestrictionFlags)).Returns(false);

        var result = manager.TryAssignToJob(Sid, ProcessHandle, JobAssignment.Restricted, "job");

        Assert.False(result.Succeeded);
        Assert.False(result.UiRestrictionsApplied);
        Assert.Equal(JobAssignmentFailureKind.UiRestrictionsFailed, result.FailureKind);
        Assert.Contains("SetUiRestrictions failed", result.FailureReason);
    }

    [Fact]
    public void TryAssignToJob_PrecreatedRestrictedJob_ReturnsFailureWithoutAssigning()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        _jobApi.Setup(a => a.CreateJobObject("job", GetExpectedRestrictedJobSecurityDescriptor()))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.GetLastWin32Error()).Returns(183);

        var result = manager.TryAssignToJob(Sid, ProcessHandle, JobAssignment.Restricted, "job");

        Assert.False(result.Succeeded);
        Assert.Equal(JobAssignmentFailureKind.PreexistingNamedJobRejected, result.FailureKind);
        Assert.Contains("already exists", result.FailureReason);
        _jobApi.Verify(a => a.AssignProcessToJobObject(It.IsAny<IntPtr>(), It.IsAny<IntPtr>()), Times.Never);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void TryAssignToJob_RestrictedSuccess_ReturnsTypedSuccess()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        _jobApi.Setup(a => a.CreateJobObject("job", GetExpectedRestrictedJobSecurityDescriptor()))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.GetLastWin32Error()).Returns(0);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, ProcessHandle)).Returns(true);
        _jobApi.Setup(a => a.SetUiRestrictions(JobHandle, ProcessJobManager.UiRestrictionFlags)).Returns(true);

        var result = manager.TryAssignToJob(Sid, ProcessHandle, JobAssignment.Restricted, "job");

        Assert.True(result.Succeeded);
        Assert.Equal(JobAssignment.Restricted, result.AssignedKind);
        Assert.Equal("job", result.JobName);
        Assert.True(result.UiRestrictionsApplied);
        Assert.True(result.LimitPolicyApplied);
        Assert.Equal(JobHandle, result.AssignedJobHandle);
    }

    [Fact]
    public void TryAssignToJob_TrackingSuccess_DoesNotReportRestrictedPolicy()
    {
        const string systemSid = "S-1-5-18";
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        _jobApi.Setup(a => a.CreateJobObject($@"Global\RunFence_Job_{systemSid}", null))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, ProcessHandle)).Returns(true);

        var result = manager.TryAssignToJob(systemSid, ProcessHandle, JobAssignment.Tracking);

        Assert.True(result.Succeeded);
        Assert.False(result.UiRestrictionsApplied);
        Assert.False(result.LimitPolicyApplied);
        _jobApi.Verify(a => a.SetUiRestrictions(It.IsAny<IntPtr>(), It.IsAny<uint>()), Times.Never);
    }

    [Fact]
    public void TryAssignToJob_ExistingRestrictedJobPolicyMismatch_EvictsBadHandleWithoutAssigning()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        var secondProcessHandle = new IntPtr(11);
        _jobApi.Setup(a => a.CreateJobObject("job", GetExpectedRestrictedJobSecurityDescriptor()))
            .Returns(JobHandle);
        _jobApi.SetupSequence(a => a.GetLastWin32Error()).Returns(0).Returns(0);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, ProcessHandle)).Returns(true);
        _jobApi.Setup(a => a.SetUiRestrictions(JobHandle, ProcessJobManager.UiRestrictionFlags)).Returns(true);

        var first = manager.TryAssignToJob(Sid, ProcessHandle, JobAssignment.Restricted, "job");
        _jobApi.Setup(a => a.QueryUiRestrictions(JobHandle)).Returns((uint?)0);

        var second = manager.TryAssignToJob(Sid, secondProcessHandle, JobAssignment.Restricted, "job");

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(JobAssignmentFailureKind.ExistingJobPolicyMismatch, second.FailureKind);
        _jobApi.Verify(a => a.AssignProcessToJobObject(JobHandle, secondProcessHandle), Times.Never);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void TryAssignToJob_ExistingRestrictedJobLosesPolicyAfterAssignment_EvictsBadHandle()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        var secondProcessHandle = new IntPtr(11);
        _jobApi.Setup(a => a.CreateJobObject("job", GetExpectedRestrictedJobSecurityDescriptor()))
            .Returns(JobHandle);
        _jobApi.SetupSequence(a => a.GetLastWin32Error()).Returns(0).Returns(0);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, ProcessHandle)).Returns(true);
        _jobApi.Setup(a => a.AssignProcessToJobObject(JobHandle, secondProcessHandle)).Returns(true);
        _jobApi.Setup(a => a.SetUiRestrictions(JobHandle, ProcessJobManager.UiRestrictionFlags)).Returns(true);

        var first = manager.TryAssignToJob(Sid, ProcessHandle, JobAssignment.Restricted, "job");
        _jobApi.SetupSequence(a => a.QueryUiRestrictions(JobHandle))
            .Returns(ProcessJobManager.UiRestrictionFlags)
            .Returns((uint?)0);

        var second = manager.TryAssignToJob(Sid, secondProcessHandle, JobAssignment.Restricted, "job");

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(JobAssignmentFailureKind.ExistingJobPolicyMismatch, second.FailureKind);
        Assert.Contains("lost the expected UI restrictions", second.FailureReason);
        _jobApi.Verify(a => a.AssignProcessToJobObject(JobHandle, secondProcessHandle), Times.Once);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void RegisterVerifiedRestrictedJob_MakesReconnectedJobQueryable()
    {
        var manager = new ProcessJobManager(_log.Object, _jobApi.Object);
        _jobApi.Setup(a => a.QueryProcessIds(JobHandle)).Returns([1234]);

        manager.RegisterVerifiedRestrictedJob(Sid, isLow: false, JobHandle);

        Assert.Equal(new HashSet<int> { 1234 }, manager.GetKeeperJobMembers(Sid, isLow: false));
        Assert.Equal(JobHandle, manager.TryGetRestrictedJobForPid(1234));
    }

    [Fact]
    public void GetRestrictedJobSecurityDescriptor_WhenUsingAdminOperationMocks_AppendsCurrentUserAce()
    {
        var descriptor = GetExpectedRestrictedJobSecurityDescriptor();

        if (!DebugHelper.UseAdminOperationMocks)
        {
            Assert.Equal(ProcessJobManager.RestrictedJobSecurityDescriptor, descriptor);
            return;
        }

        var currentSid = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(currentSid);
        Assert.StartsWith(ProcessJobManager.RestrictedJobSecurityDescriptor, descriptor, StringComparison.Ordinal);
        Assert.Contains($"(A;;GA;;;{currentSid!.Value})", descriptor, StringComparison.Ordinal);
    }

    private static string GetExpectedRestrictedJobSecurityDescriptor()
        => AdminOperationMockAccessHelper.AppendCurrentProcessGenericAllAce(
            ProcessJobManager.RestrictedJobSecurityDescriptor);
}

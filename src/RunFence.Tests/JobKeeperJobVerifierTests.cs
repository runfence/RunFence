using System.Security.Principal;
using Moq;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class JobKeeperJobVerifierTests
{
    private static readonly IntPtr JobHandle = new(30);
    private const int KeeperPid = 1234;

    private readonly Mock<IJobObjectApi> _jobApi = new();
    private readonly JobKeeperJobVerifier _verifier;
    private readonly JobKeeperInstanceIdentity _identity = new()
    {
        TargetSid = "S-1-5-21-100-200-300-1001",
        ExpectedMode = JobKeeperIntegrityMode.Restricted,
        InstanceId = "instance",
        PipeName = "pipe",
        JobName = "job",
    };

    public JobKeeperJobVerifierTests()
    {
        _verifier = new JobKeeperJobVerifier(_jobApi.Object);
    }

    [Fact]
    public void Verify_ExpectedJobMembershipLimitsAndSecurity_ReturnsTrue()
    {
        SetupValidJob();

        var result = _verifier.Verify(_identity, KeeperPid);

        Assert.True(result.Succeeded);
        Assert.Equal(JobHandle, result.JobHandle);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Never);
    }

    [Fact]
    public void Verify_MappedJobFullControlSecurity_ReturnsTrue()
    {
        SetupValidJob(security: ExpectedSecurity(accessMask: 0x001F003F));

        var result = _verifier.Verify(_identity, KeeperPid);

        Assert.True(result.Succeeded);
        Assert.Equal(JobHandle, result.JobHandle);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Never);
    }

    [Fact]
    public void Verify_NamedJobMissing_ReturnsFalse()
    {
        _jobApi.Setup(a => a.OpenJobObject(ProcessJobManager.JobObjectReconnectAccess, false, "job"))
            .Returns(IntPtr.Zero);

        Assert.False(_verifier.Verify(_identity, KeeperPid).Succeeded);
    }

    [Fact]
    public void Verify_KeeperPidNotInJob_ReturnsFalse()
    {
        SetupValidJob(processIds: [9999]);

        Assert.False(_verifier.Verify(_identity, KeeperPid).Succeeded);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_WrongUiLimits_ReturnsFalse()
    {
        SetupValidJob(uiRestrictions: JobNative.JOB_OBJECT_UILIMIT_HANDLES);

        Assert.False(_verifier.Verify(_identity, KeeperPid).Succeeded);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_WrongSecurity_ReturnsFalse()
    {
        SetupValidJob(security: new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            []));

        var result = _verifier.Verify(_identity, KeeperPid);

        Assert.False(result.Succeeded);
        Assert.Contains("job owner mismatch", result.FailureReason);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_QueryThrows_ClosesHandleAndReturnsFalse()
    {
        _jobApi.Setup(a => a.OpenJobObject(ProcessJobManager.JobObjectReconnectAccess, false, "job"))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.QueryProcessIds(JobHandle)).Throws<InvalidOperationException>();

        Assert.False(_verifier.Verify(_identity, KeeperPid).Succeeded);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    private void SetupValidJob(HashSet<int>? processIds = null, uint? uiRestrictions = null, JobObjectSecuritySnapshot? security = null)
    {
        _jobApi.Setup(a => a.OpenJobObject(ProcessJobManager.JobObjectReconnectAccess, false, "job"))
            .Returns(JobHandle);
        _jobApi.Setup(a => a.QueryProcessIds(JobHandle)).Returns(processIds ?? [KeeperPid]);
        _jobApi.Setup(a => a.QueryUiRestrictions(JobHandle))
            .Returns(uiRestrictions ?? ProcessJobManager.UiRestrictionFlags);
        _jobApi.Setup(a => a.GetSecuritySnapshot(JobHandle)).Returns(security ?? ExpectedSecurity());
    }

    private static JobObjectSecuritySnapshot ExpectedSecurity(int accessMask = 0x10000000)
    {
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        return new JobObjectSecuritySnapshot(
            administratorsSid,
            [
                new JobObjectAccessEntry(systemSid, accessMask, true),
                new JobObjectAccessEntry(administratorsSid, accessMask, true),
            ]);
    }
}

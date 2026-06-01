using Moq;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;
using RunFence.Infrastructure;
using System.Security.Principal;
using Xunit;

namespace RunFence.Tests;

public class JobKeeperJobVerifierTests
{
    private static readonly IntPtr JobHandle = new(30);
    private static readonly IntPtr OtherJobHandle = new(31);

    private readonly Mock<IJobObjectApi> _jobApi = new();
    private readonly Mock<IProcessHandleSnapshotProvider> _snapshotProvider = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly JobKeeperJobVerifier _verifier;

    public JobKeeperJobVerifierTests()
    {
        _verifier = new JobKeeperJobVerifier(
            _jobApi.Object,
            _snapshotProvider.Object,
            new VerifiedRestrictedJobAdmissionPolicy(_jobApi.Object, _log.Object));
    }

    [Fact]
    public void Verify_CarriedJobMembershipLimitsAndSecurity_ReturnsTrue()
    {
        SetupValidJob();

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.JobHandle);
        Assert.Equal(JobHandle, result.JobHandle!.Handle);
        result.JobHandle.Dispose();
    }

    [Fact]
    public void Verify_MappedJobFullControlSecurity_ReturnsTrue()
    {
        SetupValidJob(security: ExpectedSecurity(accessMask: 0x001F003F));

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.JobHandle);
        Assert.Equal(JobHandle, result.JobHandle!.Handle);
        result.JobHandle.Dispose();
    }

    [Fact]
    public void Verify_ExtraHarmlessReadAce_ReturnsTrue()
    {
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        SetupValidJob(security: new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            true,
            [
                new JobObjectAccessEntry(
                    usersSid,
                    (int)(FileSecurityNative.READ_CONTROL | KernelObjectAccessRights.Synchronize | ProcessJobManager.JobObjectQuery),
                    true),
                .. ExpectedSecurity().AccessEntries,
            ]));

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Verify_ExtraGenericReadAce_ReturnsTrue()
    {
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        SetupValidJob(security: new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            true,
            [
                new JobObjectAccessEntry(usersSid, unchecked((int)0x80000000), true),
                .. ExpectedSecurity().AccessEntries,
            ]));

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Verify_NonMatchingCandidate_IsDisposedAndSkipped()
    {
        SetupValidJob(handles: [OtherJobHandle, JobHandle]);
        _jobApi.Setup(a => a.QueryProcessIds(OtherJobHandle)).Returns([9999]);

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.JobHandle);
        Assert.Equal(JobHandle, result.JobHandle!.Handle);
        result.JobHandle.Dispose();
        _jobApi.Verify(a => a.CloseHandle(OtherJobHandle), Times.Once);
    }

    [Fact]
    public void Verify_SelectedCandidate_DisposesNonSelectedRemainingCandidates()
    {
        SetupValidJob(handles: [JobHandle, OtherJobHandle]);

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.JobHandle);
        Assert.Equal(JobHandle, result.JobHandle!.Handle);
        _jobApi.Verify(a => a.CloseHandle(OtherJobHandle), Times.Once);
        result.JobHandle.Dispose();
    }

    [Fact]
    public void Verify_NoMatchingCandidate_ReturnsFalse()
    {
        _snapshotProvider.Setup(p => p.GetJobHandleCandidates(It.IsAny<SafeProcessHandle>())).Returns([]);

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
        Assert.Contains("did not carry", result.FailureReason);
    }

    [Fact]
    public void Verify_KeeperPidNotInJob_ReturnsFalse()
    {
        SetupValidJob(processIds: [9999]);

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_WrongUiLimits_ReturnsFalse()
    {
        SetupValidJob(uiRestrictions: ProcessJobManager.JobObjectUiLimitHandles);

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_WrongSecurity_ReturnsFalse()
    {
        SetupValidJob(security: new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            true,
            []));

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
        Assert.Contains("did not carry", result.FailureReason);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_DangerousNonAdminWriteAce_ReturnsFalse()
    {
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        SetupValidJob(security: new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            true,
            [
                new JobObjectAccessEntry(usersSid, GenericWriteAccessMask, true),
                .. ExpectedSecurity().AccessEntries,
            ]));

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Verify_MissingDacl_ReturnsFalse()
    {
        SetupValidJob(security: new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            false,
            []));

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Verify_QueryThrows_ClosesHandleAndReturnsFalse()
    {
        _snapshotProvider.Setup(p => p.GetJobHandleCandidates(It.IsAny<SafeProcessHandle>()))
            .Returns([new OwnedJobHandle(_jobApi.Object, JobHandle)]);
        _jobApi.Setup(a => a.QueryProcessIds(JobHandle)).Throws<InvalidOperationException>();

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
        _jobApi.Verify(a => a.CloseHandle(JobHandle), Times.Once);
    }

    [Fact]
    public void Verify_KillOnCloseJob_ReturnsFalse()
    {
        SetupValidJob();
        _jobApi.Setup(a => a.QueryBasicLimitFlags(JobHandle)).Returns(ProcessJobManager.JobObjectLimitKillOnJobClose);

        var result = _verifier.Verify(Environment.ProcessId);

        Assert.False(result.Succeeded);
    }

    private void SetupValidJob(
        HashSet<int>? processIds = null,
        uint? uiRestrictions = null,
        JobObjectSecuritySnapshot? security = null,
        uint? basicLimitFlags = null,
        IntPtr[]? handles = null)
    {
        handles ??= [JobHandle];
        _snapshotProvider.Setup(p => p.GetJobHandleCandidates(It.IsAny<SafeProcessHandle>()))
            .Returns(handles.Select(handle => new OwnedJobHandle(_jobApi.Object, handle)).ToArray());

        foreach (var handle in handles)
        {
            _jobApi.Setup(a => a.QueryProcessIds(handle)).Returns(processIds ?? [Environment.ProcessId]);
            _jobApi.Setup(a => a.QueryUiRestrictions(handle))
                .Returns(uiRestrictions ?? ProcessJobManager.UiRestrictionFlags);
            _jobApi.Setup(a => a.GetSecuritySnapshot(handle)).Returns(security ?? ExpectedSecurity());
            _jobApi.Setup(a => a.QueryBasicLimitFlags(handle)).Returns(basicLimitFlags ?? 0u);
        }
    }

    private static JobObjectSecuritySnapshot ExpectedSecurity(int accessMask = 0x10000000)
    {
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        return new JobObjectSecuritySnapshot(
            administratorsSid,
            true,
            [
                new JobObjectAccessEntry(systemSid, accessMask, true),
                new JobObjectAccessEntry(administratorsSid, accessMask, true),
            ]);
    }

    private const int GenericWriteAccessMask = 0x40000000;
}

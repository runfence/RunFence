using System.IO.Pipes;
using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperProcessVerifierTests
{
    private const string JobKeeperExePath = @"C:\Program Files\RunFence\RunFence.JobKeeper.exe";
    private readonly Mock<IJobKeeperJobVerifier> _jobVerifier = new();
    private readonly Mock<IJobKeeperClientProcessQuery> _clientQuery = new();
    private readonly Mock<IProcessJobManager> _processJobManager = new();
    private readonly Mock<IVerifiedRestrictedJobCache> _cache = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly SecurityIdentifier _currentUserSid = new(WellKnownSidType.BuiltinUsersSid, null);

    [Fact]
    public void Verify_RegistersVerifiedRestrictedJob_WhenVerifierSucceeds()
    {
        using var pipe = CreatePipe();
        var identity = Identity(JobKeeperIntegrityMode.Restricted);
        var jobApi = new Mock<IJobObjectApi>();
        var jobHandle = new IntPtr(40);
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, NativeTokenHelper.MandatoryLevelMedium));
        _jobVerifier.Setup(v => v.Verify(123))
            .Returns(JobKeeperJobVerificationResult.Success(new OwnedJobHandle(jobApi.Object, jobHandle)));
        _cache.Setup(c => c.TryAddDuplicate(jobHandle)).Returns(true);

        var sut = CreateSut();

        var result = sut.Verify(pipe, expectedPid: 9999, _currentUserSid, identity);

        Assert.True(result.Succeeded);
        Assert.Equal(123, result.ProcessId);
        _processJobManager.Verify(m => m.RegisterVerifiedRestrictedJob(identity.TargetSid, false, jobHandle), Times.Once);
        _cache.Verify(c => c.TryAddDuplicate(jobHandle), Times.Once);
        jobApi.Verify(a => a.CloseHandle(It.IsAny<IntPtr>()), Times.Never);
    }

    [Fact]
    public void Verify_WhenPipeClientProcessIdUnavailable_FailsBeforeFurtherChecks()
    {
        using var pipe = CreatePipe();
        _clientQuery.Setup(q => q.TryGetPipeClientProcessId(pipe, out It.Ref<uint>.IsAny)).Returns(false);

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.False(result.Succeeded);
        Assert.Contains("PID was unavailable", result.FailureReason);
        _clientQuery.Verify(q => q.QueryProcessInfo(It.IsAny<uint>()), Times.Never);
        VerifyNoRegistrationOrCache();
        _jobVerifier.Verify(v => v.Verify(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Verify_WhenImagePathUnavailable_FailsClosed()
    {
        using var pipe = CreatePipe();
        SetupClientInfo(123, new JobKeeperClientProcessInfo(null, _currentUserSid, NativeTokenHelper.MandatoryLevelMedium));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.False(result.Succeeded);
        Assert.Contains("QueryFullProcessImageName failed", result.FailureReason);
        VerifyNoPreRegistrationSideEffects();
    }

    [Fact]
    public void Verify_WhenImagePathMismatches_FailsClosed()
    {
        using var pipe = CreatePipe();
        SetupClientInfo(123, new JobKeeperClientProcessInfo(@"C:\wrong\RunFence.JobKeeper.exe", _currentUserSid, NativeTokenHelper.MandatoryLevelMedium));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.False(result.Succeeded);
        Assert.Contains("image mismatch", result.FailureReason);
        VerifyNoPreRegistrationSideEffects();
    }

    [Fact]
    public void Verify_WhenOwnerUnavailable_FailsClosed()
    {
        using var pipe = CreatePipe();
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, null, NativeTokenHelper.MandatoryLevelMedium));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.False(result.Succeeded);
        Assert.Contains("<unavailable>", result.FailureReason);
        VerifyNoPreRegistrationSideEffects();
    }

    [Fact]
    public void Verify_WhenOwnerMismatches_FailsClosed()
    {
        using var pipe = CreatePipe();
        var otherSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, otherSid, NativeTokenHelper.MandatoryLevelMedium));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.False(result.Succeeded);
        Assert.Contains("owner SID mismatch", result.FailureReason);
        VerifyNoPreRegistrationSideEffects();
    }

    [Theory]
    [InlineData(JobKeeperIntegrityMode.Restricted, NativeTokenHelper.MandatoryLevelLow)]
    [InlineData(JobKeeperIntegrityMode.LowIntegrity, NativeTokenHelper.MandatoryLevelMedium)]
    public void Verify_WhenIntegrityMismatches_FailsClosed(JobKeeperIntegrityMode expectedMode, int actualIntegrity)
    {
        using var pipe = CreatePipe();
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, actualIntegrity));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(expectedMode));

        Assert.False(result.Succeeded);
        Assert.Contains("integrity mismatch", result.FailureReason);
        VerifyNoPreRegistrationSideEffects();
    }

    [Fact]
    public void Verify_WhenIntegrityUnavailableAndRestrictedModeExpected_Succeeds()
    {
        using var pipe = CreatePipe();
        var jobApi = new Mock<IJobObjectApi>();
        var jobHandle = new IntPtr(50);
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, null));
        _jobVerifier.Setup(v => v.Verify(123))
            .Returns(JobKeeperJobVerificationResult.Success(new OwnedJobHandle(jobApi.Object, jobHandle)));
        _cache.Setup(c => c.TryAddDuplicate(jobHandle)).Returns(true);

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.True(result.Succeeded);
        _processJobManager.Verify(m => m.RegisterVerifiedRestrictedJob(It.IsAny<string>(), false, jobHandle), Times.Once);
    }

    [Fact]
    public void Verify_WhenIntegrityUnavailableAndLowIntegrityExpected_FailsClosed()
    {
        using var pipe = CreatePipe();
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, null));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.LowIntegrity));

        Assert.False(result.Succeeded);
        Assert.Contains("integrity mismatch", result.FailureReason);
        VerifyNoPreRegistrationSideEffects();
    }

    [Fact]
    public void Verify_WhenJobVerifierFails_FailsClosed()
    {
        using var pipe = CreatePipe();
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, NativeTokenHelper.MandatoryLevelMedium));
        _jobVerifier.Setup(v => v.Verify(123)).Returns(JobKeeperJobVerificationResult.Failure("bad job"));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, Identity(JobKeeperIntegrityMode.Restricted));

        Assert.False(result.Succeeded);
        Assert.Contains("job verification failed", result.FailureReason);
        VerifyNoRegistrationOrCache();
    }

    [Fact]
    public void Verify_WhenRegistrationFails_DisposesVerifiedJobHandleAndSkipsCache()
    {
        using var pipe = CreatePipe();
        var identity = Identity(JobKeeperIntegrityMode.Restricted);
        var jobApi = new Mock<IJobObjectApi>();
        var jobHandle = new IntPtr(60);
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, NativeTokenHelper.MandatoryLevelMedium));
        _jobVerifier.Setup(v => v.Verify(123))
            .Returns(JobKeeperJobVerificationResult.Success(new OwnedJobHandle(jobApi.Object, jobHandle)));
        _processJobManager.Setup(m => m.RegisterVerifiedRestrictedJob(identity.TargetSid, false, jobHandle))
            .Throws(new InvalidOperationException("registration failed"));

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, identity);

        Assert.False(result.Succeeded);
        Assert.Contains("registration failed", result.FailureReason);
        _processJobManager.Verify(m => m.RegisterVerifiedRestrictedJob(identity.TargetSid, false, jobHandle), Times.Once);
        _cache.Verify(c => c.TryAddDuplicate(It.IsAny<IntPtr>()), Times.Never);
        jobApi.Verify(a => a.CloseHandle(jobHandle), Times.Once);
    }

    [Fact]
    public void Verify_WhenCacheAdmissionFails_LogsWarningAndStillSucceeds()
    {
        using var pipe = CreatePipe();
        var identity = Identity(JobKeeperIntegrityMode.Restricted);
        var jobApi = new Mock<IJobObjectApi>();
        var jobHandle = new IntPtr(70);
        SetupClientInfo(123, new JobKeeperClientProcessInfo(JobKeeperExePath, _currentUserSid, NativeTokenHelper.MandatoryLevelMedium));
        _jobVerifier.Setup(v => v.Verify(123))
            .Returns(JobKeeperJobVerificationResult.Success(new OwnedJobHandle(jobApi.Object, jobHandle)));
        _cache.Setup(c => c.TryAddDuplicate(jobHandle)).Returns(false);

        var result = CreateSut().Verify(pipe, 0, _currentUserSid, identity);

        Assert.True(result.Succeeded);
        _processJobManager.Verify(m => m.RegisterVerifiedRestrictedJob(identity.TargetSid, false, jobHandle), Times.Once);
        _cache.Verify(c => c.TryAddDuplicate(jobHandle), Times.Once);
        _log.Verify(
            l => l.Warn(It.Is<string>(message => message.Contains("failed to add verified restricted job", StringComparison.Ordinal))),
            Times.Once);
        jobApi.Verify(a => a.CloseHandle(It.IsAny<IntPtr>()), Times.Never);
    }

    private JobKeeperProcessVerifier CreateSut() =>
        new(
            _jobVerifier.Object,
            _clientQuery.Object,
            _processJobManager.Object,
            _cache.Object,
            _log.Object,
            JobKeeperExePath);

    private void SetupClientInfo(uint clientPid, JobKeeperClientProcessInfo info)
    {
        _clientQuery.Setup(q => q.TryGetPipeClientProcessId(It.IsAny<NamedPipeServerStream>(), out It.Ref<uint>.IsAny))
            .Callback(new TryGetPipeClientProcessIdCallback((NamedPipeServerStream _, out uint pid) => pid = clientPid))
            .Returns(true);
        _clientQuery.Setup(q => q.QueryProcessInfo(clientPid)).Returns(info);
    }

    private void VerifyNoPreRegistrationSideEffects()
    {
        _jobVerifier.Verify(v => v.Verify(It.IsAny<int>()), Times.Never);
        VerifyNoRegistrationOrCache();
    }

    private void VerifyNoRegistrationOrCache()
    {
        _processJobManager.Verify(m => m.RegisterVerifiedRestrictedJob(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IntPtr>()), Times.Never);
        _cache.Verify(c => c.TryAddDuplicate(It.IsAny<IntPtr>()), Times.Never);
    }

    private JobKeeperInstanceIdentity Identity(JobKeeperIntegrityMode mode) => new()
    {
        TargetSid = _currentUserSid.Value,
        ExpectedMode = mode,
        InstanceId = "instance",
        PipeName = "pipe",
    };

    private static NamedPipeServerStream CreatePipe() =>
        new($"RunFenceTest_JobKeeperProcessVerifier_{Guid.NewGuid():N}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private delegate void TryGetPipeClientProcessIdCallback(NamedPipeServerStream pipe, out uint clientPid);
}

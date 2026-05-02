using System.IO.Pipes;
using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public sealed class RestrictedJobLaunchCoordinatorTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private static readonly IntPtr TokenHandle = new(10);
    private static readonly IntPtr KeeperProcessHandle = new(20);
    private static readonly IntPtr KeeperThreadHandle = new(21);
    private static readonly IntPtr JobHandle = new(30);

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IProcessJobManager> _processJobManager = new();
    private readonly Mock<IJobKeeperService> _jobKeeperService = new();
    private readonly Mock<IJobKeeperIdentityStore> _identityStore = new();
    private readonly Mock<IJobKeeperPipeServerFactory> _pipeServerFactory = new();
    private readonly Mock<IJobKeeperLaunchIpcClient> _launchIpcClient = new();
    private readonly Mock<IJobObjectApi> _jobObjectApi = new();
    private readonly Mock<IRestrictedProcessControl> _processControl = new();
    private readonly Mock<IJobKeeperLaunchProcessApi> _launchProcessApi = new();
    private readonly Mock<IRestrictedJobProcessLauncher> _processLauncher = new();
    private readonly RestrictedJobLaunchCoordinator _coordinator;

    public RestrictedJobLaunchCoordinatorTests()
    {
        _processControl.Setup(c => c.ResumeThread(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny))
            .Returns((IntPtr _, out int error) =>
            {
                error = 0;
                return true;
            });

        _coordinator = new RestrictedJobLaunchCoordinator(
            _log.Object,
            _processJobManager.Object,
            _jobKeeperService.Object,
            _identityStore.Object,
            _pipeServerFactory.Object,
            _launchIpcClient.Object,
            _jobObjectApi.Object,
            new RestrictedProcessActivationGuard(_processControl.Object),
            _launchProcessApi.Object,
            @"C:\RunFence\RunFence.JobKeeper.exe");
        _coordinator.Initialize(_processLauncher.Object);

        _launchProcessApi.Setup(a => a.OpenLaunchedProcess(It.IsAny<int>()))
            .Returns(new IntPtr(700));
    }

    [Fact]
    public void SeedJobKeeperAndLaunch_FirstSeed_AssignsKeeperRegistersAndLaunchesThroughKeeper()
    {
        var identity = Identity(isLow: false);
        using var pipe = CreatePipe();
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe", "--flag", @"C:\Apps", HideWindow: true);

        SetupSuccessfulSeed(identity, pipe, isLow: false);
        _launchIpcClient.Setup(s => s.SendLaunchRequest(Sid, false, It.Is<JobKeeperLaunchRequest>(r =>
                r.ExePath == target.ExePath && r.Arguments == target.Arguments && r.WorkingDirectory == target.WorkingDirectory && r.HideWindow)))
            .Returns(Environment.ProcessId);

        var result = _coordinator.SeedJobKeeperAndLaunch(TokenHandle, LaunchTokenSource.Credentials, Sid, false, target);

        Assert.Equal((uint)Environment.ProcessId, result.dwProcessId);
        _processJobManager.Verify(m => m.ResetJobHandle(Sid, JobAssignment.Restricted), Times.Once);
        _processJobManager.Verify(m => m.TryAssignToJob(Sid, KeeperProcessHandle, JobAssignment.Restricted, identity.JobName), Times.Once);
        _jobKeeperService.Verify(s => s.WaitAndRegisterJobKeeper(identity, pipe, 1234, It.Is<SecurityIdentifier>(sid => sid.Value == Sid)), Times.Once);
        _processControl.Verify(c => c.ResumeThread(KeeperThreadHandle, out It.Ref<int>.IsAny), Times.Once);
        _identityStore.Verify(s => s.Remove(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SeedJobKeeperAndLaunch_ExistingKeeperReconnect_LaunchesWithoutSeeding()
    {
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe", (string?)null, null, HideWindow: false);
        _jobKeeperService.Setup(s => s.TryReconnectExistingJobKeeper(Sid, false, It.IsAny<SecurityIdentifier>()))
            .Returns(5678);
        _launchIpcClient.Setup(s => s.SendLaunchRequest(Sid, false, It.IsAny<JobKeeperLaunchRequest>()))
            .Returns(Environment.ProcessId);

        var result = _coordinator.SeedJobKeeperAndLaunch(TokenHandle, LaunchTokenSource.Credentials, Sid, false, target);

        Assert.Equal((uint)Environment.ProcessId, result.dwProcessId);
        _identityStore.Verify(s => s.CreateFresh(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        _processLauncher.Verify(l => l.LaunchWithPreparedToken(
            It.IsAny<IntPtr>(),
            It.IsAny<ProcessLaunchTarget>(),
            It.IsAny<LaunchTokenSource>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SeedJobKeeperAndLaunch_LowIntegrity_UsesLowIdentityAndJobAssignment()
    {
        var identity = Identity(isLow: true);
        using var pipe = CreatePipe();
        SetupSuccessfulSeed(identity, pipe, isLow: true);
        _launchIpcClient.Setup(s => s.SendLaunchRequest(Sid, true, It.IsAny<JobKeeperLaunchRequest>()))
            .Returns(Environment.ProcessId);

        var result = _coordinator.SeedJobKeeperAndLaunch(
            TokenHandle,
            LaunchTokenSource.Credentials,
            Sid,
            true,
            new ProcessLaunchTarget(@"C:\Apps\App.exe", (string?)null, null, HideWindow: false));

        Assert.Equal((uint)Environment.ProcessId, result.dwProcessId);
        _identityStore.Verify(s => s.CreateFresh(Sid, true), Times.Once);
        _processJobManager.Verify(m => m.ResetJobHandle(Sid, JobAssignment.LowIntegrity), Times.Once);
        _processJobManager.Verify(m => m.TryAssignToJob(Sid, KeeperProcessHandle, JobAssignment.LowIntegrity, identity.JobName), Times.Once);
    }

    [Fact]
    public void SeedJobKeeperAndLaunch_KeeperRegistrationFails_RemovesIdentityClosesHandlesAndFailsClosed()
    {
        var identity = Identity(isLow: false);
        using var pipe = CreatePipe();
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe", (string?)null, null, HideWindow: false);

        SetupSuccessfulSeed(identity, pipe, isLow: false);
        _jobKeeperService.Setup(s => s.WaitAndRegisterJobKeeper(identity, pipe, 1234, It.IsAny<SecurityIdentifier>()))
            .Returns(0);

        var ex = Assert.Throws<LaunchFailedException>(() =>
            _coordinator.SeedJobKeeperAndLaunch(TokenHandle, LaunchTokenSource.Credentials, Sid, false, target));

        Assert.Contains("JobKeeper failed to register", ex.Message);
        _processControl.Verify(c => c.TerminateProcess(It.IsAny<IntPtr>()), Times.Never);
        _processControl.Verify(c => c.CloseHandle(KeeperProcessHandle), Times.Once);
        _processJobManager.Verify(m => m.ResetJobHandle(Sid, JobAssignment.Restricted), Times.Exactly(2));
        _identityStore.Verify(s => s.Remove(Sid, false), Times.Once);
        _processLauncher.Verify(l => l.LaunchWithPreparedToken(
                It.IsAny<IntPtr>(),
                It.Is<ProcessLaunchTarget>(t => t.ExePath == target.ExePath),
                It.IsAny<LaunchTokenSource>(),
                It.IsAny<string>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    private void SetupSuccessfulSeed(JobKeeperInstanceIdentity identity, NamedPipeServerStream pipe, bool isLow)
    {
        _jobKeeperService.Setup(s => s.TryReconnectExistingJobKeeper(Sid, isLow, It.IsAny<SecurityIdentifier>()))
            .Returns(0);
        _identityStore.Setup(s => s.CreateFresh(Sid, isLow)).Returns(identity);
        _pipeServerFactory.Setup(s => s.Create(identity, It.IsAny<SecurityIdentifier>())).Returns(pipe);
        _processLauncher.Setup(l => l.LaunchWithPreparedToken(
                TokenHandle,
                It.Is<ProcessLaunchTarget>(t => t.ExePath.EndsWith("RunFence.JobKeeper.exe", StringComparison.OrdinalIgnoreCase)),
                LaunchTokenSource.Credentials,
                Sid,
                false))
            .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
            {
                hProcess = KeeperProcessHandle,
                hThread = KeeperThreadHandle,
                dwProcessId = 1234,
            });
        _processJobManager.Setup(m => m.TryAssignToJob(Sid, KeeperProcessHandle, It.IsAny<JobAssignment>(), identity.JobName))
            .Returns((string _, IntPtr _, JobAssignment assignment, string? jobName) =>
                JobAssignmentResult.Success(assignment, assignment, jobName!, 1234, true, true, JobHandle));
        _jobObjectApi.Setup(a => a.DuplicateHandleToProcess(
                It.IsAny<IntPtr>(),
                JobHandle,
                KeeperProcessHandle,
                ProcessJobManager.JobObjectKeepAliveAccess))
            .Returns(true);
        _jobKeeperService.Setup(s => s.WaitAndRegisterJobKeeper(identity, pipe, 1234, It.IsAny<SecurityIdentifier>()))
            .Returns(1234);
    }

    private static JobKeeperInstanceIdentity Identity(bool isLow) => new()
    {
        TargetSid = Sid,
        ExpectedMode = JobKeeperInstanceIdentity.GetMode(isLow),
        InstanceId = isLow ? "low" : "restricted",
        PipeName = $"pipe-{Guid.NewGuid():N}",
        JobName = $"job-{Guid.NewGuid():N}",
    };

    private static NamedPipeServerStream CreatePipe() =>
        new($"test-{Guid.NewGuid():N}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}

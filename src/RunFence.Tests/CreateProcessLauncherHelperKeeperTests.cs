using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public sealed class CreateProcessLauncherHelperKeeperTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private static readonly IntPtr FakeLaunchedProcessHandle = new(7001);
    private static readonly IntPtr FakeLaunchedThreadHandle = new(7002);

    [Fact]
    public void LaunchUsingAcquiredToken_ExistingKeeperFastPath_BypassesTokenPreparation()
    {
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenPrivilegeStateReader>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            new Mock<IProcessJobManager>().Object,
            new Mock<IProcessControl>().Object,
            () => new Mock<ITrackingJobStateStore>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IPreparedTokenProcessLauncher>().Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var expected = TestProcessInfoFactory.Native(new ProcessLaunchNative.PROCESS_INFORMATION
        {
            hProcess = IntPtr.Zero,
            dwProcessId = 4321,
        });
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Isolated,
        };

        jobKeeperService.Setup(s => s.HasJobKeeper(Sid, false)).Returns(true);
        restrictedCoordinator.Setup(c => c.LaunchViaJobKeeper(Sid, false, target)).Returns(expected);

        var result = helper.LaunchUsingAcquiredToken(IntPtr.Zero, target, identity);

        Assert.Same(expected, result);
        restrictedCoordinator.Verify(c => c.LaunchViaJobKeeper(Sid, false, target), Times.Once);
        restrictedCoordinator.Verify(c => c.SeedJobKeeperAndLaunch(
            It.IsAny<IntPtr>(),
            It.IsAny<LaunchTokenSource>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchUsingAcquiredToken_StaleKeeperFastPathFailure_FallsBackToReseedInSameAttempt()
    {
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var saferDeElevationHelper = new Mock<ISaferDeElevationHelper>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            saferDeElevationHelper.Object,
            new Mock<ITokenPrivilegeStateReader>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            new Mock<IProcessJobManager>().Object,
            new Mock<IProcessControl>().Object,
            () => new Mock<ITrackingJobStateStore>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IPreparedTokenProcessLauncher>().Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Isolated,
        };
        var currentToken = OpenCurrentProcessToken();

        try
        {
            jobKeeperService.Setup(s => s.HasJobKeeper(Sid, false)).Returns(true);
            restrictedCoordinator.Setup(c => c.LaunchViaJobKeeper(Sid, false, target))
                .Throws(new StaleJobKeeperException(Sid));
            restrictedCoordinator.Setup(c => c.SeedJobKeeperAndLaunch(
                    It.IsAny<IntPtr>(),
                    LaunchTokenSource.CurrentProcess,
                    Sid,
                    false,
                    target))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = IntPtr.Zero,
                    dwProcessId = 5678,
                });

            var result = helper.LaunchUsingAcquiredToken(currentToken, target, identity);

            Assert.Equal(5678, result!.Id);
            restrictedCoordinator.Verify(c => c.LaunchViaJobKeeper(Sid, false, target), Times.Once);
            restrictedCoordinator.Verify(c => c.SeedJobKeeperAndLaunch(
                It.IsAny<IntPtr>(),
                LaunchTokenSource.CurrentProcess,
                Sid,
                false,
                target), Times.Once);
            saferDeElevationHelper.Verify(s => s.CreateDeElevatedToken(It.IsAny<IntPtr>()), Times.Never);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Fact]
    public void LaunchUsingAcquiredToken_NonStaleKeeperInvalidOperation_DoesNotFallback()
    {
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenPrivilegeStateReader>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            new Mock<IProcessJobManager>().Object,
            new Mock<IProcessControl>().Object,
            () => new Mock<ITrackingJobStateStore>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IPreparedTokenProcessLauncher>().Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Isolated,
        };

        jobKeeperService.Setup(s => s.HasJobKeeper(Sid, false)).Returns(true);
        restrictedCoordinator.Setup(c => c.LaunchViaJobKeeper(Sid, false, target))
            .Throws(new InvalidOperationException("OpenLaunchedProcess failed."));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            helper.LaunchUsingAcquiredToken(IntPtr.Zero, target, identity));

        Assert.Equal("OpenLaunchedProcess failed.", ex.Message);
        restrictedCoordinator.Verify(c => c.SeedJobKeeperAndLaunch(
            It.IsAny<IntPtr>(),
            It.IsAny<LaunchTokenSource>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchUsingAcquiredToken_TrackingStateSaveFailure_PropagatesSaveFailure()
    {
        var processJobManager = new Mock<IProcessJobManager>();
        var processControl = new Mock<IProcessControl>();
        var trackingJobStateStore = new Mock<ITrackingJobStateStore>();
        var preparedLauncher = new Mock<IPreparedTokenProcessLauncher>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenPrivilegeStateReader>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            processJobManager.Object,
            processControl.Object,
            () => trackingJobStateStore.Object,
            new Mock<IJobKeeperService>().Object,
            new Mock<IRestrictedJobLaunchCoordinator>().Object,
            preparedLauncher.Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
        };
        var currentToken = OpenCurrentProcessToken();

        try
        {
            preparedLauncher
                .Setup(p => p.LaunchWithPreparedToken(It.IsAny<IntPtr>(), target, LaunchTokenSource.CurrentProcess, Sid))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = FakeLaunchedProcessHandle,
                    hThread = FakeLaunchedThreadHandle,
                    dwProcessId = 4321,
                });
            processJobManager
                .Setup(m => m.TryAssignToJob(Sid, FakeLaunchedProcessHandle, JobAssignment.Tracking, null))
                .Returns(JobAssignmentResult.Success(
                    JobAssignment.Tracking,
                    JobAssignment.Tracking,
                    "tracking-job",
                    4321,
                    uiRestrictionsApplied: false,
                    limitPolicyApplied: true,
                    assignedJobHandle: IntPtr.Zero));
            trackingJobStateStore
                .Setup(store => store.AddTrackingJobSid(Sid))
                .Throws(new IOException("disk full"));

            var ex = Assert.Throws<IOException>(() =>
                helper.LaunchUsingAcquiredToken(currentToken, target, identity));

            Assert.Equal("disk full", ex.Message);
            trackingJobStateStore.Verify(store => store.AddTrackingJobSid(Sid), Times.Once);
            processControl.Verify(c => c.ResumeThread(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny), Times.Never);
            processControl.Verify(c => c.TerminateProcessBestEffort(FakeLaunchedProcessHandle, 1), Times.Once);
            processControl.Verify(c => c.CloseHandle(FakeLaunchedThreadHandle), Times.Once);
            processControl.Verify(c => c.CloseHandle(FakeLaunchedProcessHandle), Times.Once);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Fact]
    public void LaunchUsingAcquiredToken_TrackingStateSaveSuccess_ResumesPersistsAndTransfersOwnedHandles()
    {
        var processJobManager = new Mock<IProcessJobManager>();
        var processControl = new Mock<IProcessControl>();
        var trackingJobStateStore = new Mock<ITrackingJobStateStore>();
        var preparedLauncher = new Mock<IPreparedTokenProcessLauncher>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenPrivilegeStateReader>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            processJobManager.Object,
            processControl.Object,
            () => trackingJobStateStore.Object,
            new Mock<IJobKeeperService>().Object,
            new Mock<IRestrictedJobLaunchCoordinator>().Object,
            preparedLauncher.Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
        };
        var currentToken = OpenCurrentProcessToken();

        try
        {
            preparedLauncher
                .Setup(p => p.LaunchWithPreparedToken(It.IsAny<IntPtr>(), target, LaunchTokenSource.CurrentProcess, Sid))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = FakeLaunchedProcessHandle,
                    hThread = FakeLaunchedThreadHandle,
                    dwProcessId = 9876,
                });
            processJobManager
                .Setup(m => m.TryAssignToJob(Sid, FakeLaunchedProcessHandle, JobAssignment.Tracking, null))
                .Returns(JobAssignmentResult.Success(
                    JobAssignment.Tracking,
                    JobAssignment.Tracking,
                    "tracking-job",
                    9876,
                    uiRestrictionsApplied: false,
                    limitPolicyApplied: true,
                    assignedJobHandle: IntPtr.Zero));
            processControl
                .Setup(c => c.ResumeThread(FakeLaunchedThreadHandle, out It.Ref<int>.IsAny))
                .Returns((IntPtr _, out int error) =>
                {
                    error = 0;
                    return true;
                });

            using (var result = helper.LaunchUsingAcquiredToken(currentToken, target, identity))
            {
                Assert.NotNull(result);
                Assert.Equal(9876, result!.Id);
            }

            trackingJobStateStore.Verify(store => store.AddTrackingJobSid(Sid), Times.Once);
            processControl.Verify(c => c.ResumeThread(FakeLaunchedThreadHandle, out It.Ref<int>.IsAny), Times.Once);
            processControl.Verify(c => c.TerminateProcessBestEffort(It.IsAny<IntPtr>(), It.IsAny<uint>()), Times.Never);
            processControl.Verify(c => c.CloseHandle(FakeLaunchedProcessHandle), Times.Never);
            processControl.Verify(c => c.CloseHandle(FakeLaunchedThreadHandle), Times.Never);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Fact]
    public void LaunchUsingAcquiredToken_SkippedTrackingAssignment_DoesNotPersistTrackingState()
    {
        var processJobManager = new Mock<IProcessJobManager>();
        var trackingJobStateStore = new Mock<ITrackingJobStateStore>();
        var preparedLauncher = new Mock<IPreparedTokenProcessLauncher>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenPrivilegeStateReader>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            processJobManager.Object,
            new Mock<IProcessControl>().Object,
            () => trackingJobStateStore.Object,
            new Mock<IJobKeeperService>().Object,
            new Mock<IRestrictedJobLaunchCoordinator>().Object,
            preparedLauncher.Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
        };
        var currentToken = OpenCurrentProcessToken();
        var processHandle = OpenCurrentProcessHandle();

        try
        {
            preparedLauncher
                .Setup(p => p.LaunchWithPreparedToken(It.IsAny<IntPtr>(), target, LaunchTokenSource.CurrentProcess, Sid))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = processHandle,
                    hThread = IntPtr.Zero,
                    dwProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id,
                });
            processJobManager
                .Setup(m => m.TryAssignToJob(Sid, processHandle, JobAssignment.Tracking, null))
                .Returns(JobAssignmentResult.Skipped(JobAssignment.Tracking, "not needed"));

            using var process = helper.LaunchUsingAcquiredToken(currentToken, target, identity);

            Assert.NotNull(process);
            trackingJobStateStore.Verify(store => store.AddTrackingJobSid(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Theory]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.HighIntegrity)]
    public void LaunchUsingAcquiredToken_HighIntegrityRequest_SetsHighIntegrityAndSkipsJobKeeper(
        PrivilegeLevel privilegeLevel)
    {
        var tokenIntegrityService = new Mock<ITokenIntegrityLevelService>();
        var tokenPrivilegeStateReader = new Mock<ITokenPrivilegeStateReader>();
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var preparedLauncher = new Mock<IPreparedTokenProcessLauncher>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            tokenPrivilegeStateReader.Object,
            tokenIntegrityService.Object,
            new Mock<IProcessJobManager>().Object,
            new Mock<IProcessControl>().Object,
            () => new Mock<ITrackingJobStateStore>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            preparedLauncher.Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = privilegeLevel,
        };
        var currentToken = OpenCurrentProcessToken();

        try
        {
            preparedLauncher
                .Setup(p => p.LaunchWithPreparedToken(It.IsAny<IntPtr>(), target, LaunchTokenSource.CurrentProcess, Sid))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = IntPtr.Zero,
                    dwProcessId = 2468,
                });

            var result = helper.LaunchUsingAcquiredToken(currentToken, target, identity);

            Assert.Equal(2468, result!.Id);
            tokenIntegrityService.Verify(
                service => service.SetHighIntegrity(It.IsAny<IntPtr>(), out It.Ref<IntPtr>.IsAny, out It.Ref<IntPtr>.IsAny),
                Times.Once);
            tokenPrivilegeStateReader.Verify(
                reader => reader.TryGetIntegrityLevel(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny),
                Times.Never);
            jobKeeperService.Verify(service => service.HasJobKeeper(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            restrictedCoordinator.Verify(
                coordinator => coordinator.SeedJobKeeperAndLaunch(
                    It.IsAny<IntPtr>(),
                    It.IsAny<LaunchTokenSource>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<ProcessLaunchTarget>()),
                Times.Never);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Fact]
    public void LaunchUsingAcquiredToken_HighestAllowed_ElevatedHighIntegrityToken_SkipsSetHighIntegrity()
    {
        var tokenIntegrityService = new Mock<ITokenIntegrityLevelService>();
        var tokenPrivilegeStateReader = new Mock<ITokenPrivilegeStateReader>();
        var preparedLauncher = new Mock<IPreparedTokenProcessLauncher>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            tokenPrivilegeStateReader.Object,
            tokenIntegrityService.Object,
            new Mock<IProcessJobManager>().Object,
            new Mock<IProcessControl>().Object,
            () => new Mock<ITrackingJobStateStore>().Object,
            new Mock<IJobKeeperService>().Object,
            new Mock<IRestrictedJobLaunchCoordinator>().Object,
            preparedLauncher.Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
        };
        var currentToken = OpenCurrentProcessToken();
        var highIntegrityLevel = NativeTokenHelper.MandatoryLevelHigh;

        try
        {
            tokenPrivilegeStateReader.Setup(reader => reader.IsElevated(It.IsAny<IntPtr>())).Returns(true);
            tokenPrivilegeStateReader
                .Setup(reader => reader.TryGetIntegrityLevel(It.IsAny<IntPtr>(), out highIntegrityLevel))
                .Returns(true);
            preparedLauncher
                .Setup(p => p.LaunchWithPreparedToken(It.IsAny<IntPtr>(), target, LaunchTokenSource.CurrentProcess, Sid))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = IntPtr.Zero,
                    dwProcessId = 1357,
                });

            var result = helper.LaunchUsingAcquiredToken(currentToken, target, identity);

            Assert.Equal(1357, result!.Id);
            tokenPrivilegeStateReader.Verify(reader => reader.IsElevated(It.IsAny<IntPtr>()), Times.Once);
            tokenPrivilegeStateReader.Verify(
                reader => reader.TryGetIntegrityLevel(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny),
                Times.Once);
            tokenIntegrityService.Verify(
                service => service.SetHighIntegrity(It.IsAny<IntPtr>(), out It.Ref<IntPtr>.IsAny, out It.Ref<IntPtr>.IsAny),
                Times.Never);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Fact]
    public void LaunchUsingAcquiredToken_HighestAllowed_ElevatedMediumIntegrityToken_SetsHighIntegrity()
    {
        var tokenIntegrityService = new Mock<ITokenIntegrityLevelService>();
        var tokenPrivilegeStateReader = new Mock<ITokenPrivilegeStateReader>();
        var preparedLauncher = new Mock<IPreparedTokenProcessLauncher>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            tokenPrivilegeStateReader.Object,
            tokenIntegrityService.Object,
            new Mock<IProcessJobManager>().Object,
            new Mock<IProcessControl>().Object,
            () => new Mock<ITrackingJobStateStore>().Object,
            new Mock<IJobKeeperService>().Object,
            new Mock<IRestrictedJobLaunchCoordinator>().Object,
            preparedLauncher.Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
        };
        var currentToken = OpenCurrentProcessToken();
        var mediumIntegrityLevel = NativeTokenHelper.MandatoryLevelMedium;

        try
        {
            tokenPrivilegeStateReader.Setup(reader => reader.IsElevated(It.IsAny<IntPtr>())).Returns(true);
            tokenPrivilegeStateReader
                .Setup(reader => reader.TryGetIntegrityLevel(It.IsAny<IntPtr>(), out mediumIntegrityLevel))
                .Returns(true);
            preparedLauncher
                .Setup(p => p.LaunchWithPreparedToken(It.IsAny<IntPtr>(), target, LaunchTokenSource.CurrentProcess, Sid))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = IntPtr.Zero,
                    dwProcessId = 8642,
                });

            var result = helper.LaunchUsingAcquiredToken(currentToken, target, identity);

            Assert.Equal(8642, result!.Id);
            tokenPrivilegeStateReader.Verify(
                reader => reader.TryGetIntegrityLevel(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny),
                Times.Once);
            tokenIntegrityService.Verify(
                service => service.SetHighIntegrity(It.IsAny<IntPtr>(), out It.Ref<IntPtr>.IsAny, out It.Ref<IntPtr>.IsAny),
                Times.Once);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    private static IntPtr OpenCurrentProcessToken()
    {
        if (!ProcessNative.OpenProcessToken(
                System.Diagnostics.Process.GetCurrentProcess().Handle,
                ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY,
                out var token))
        {
            throw new InvalidOperationException("Unable to open current process token for test setup.");
        }

        return token;
    }

    private static IntPtr OpenCurrentProcessHandle()
    {
        var handle = ProcessNative.OpenProcess(
            ProcessLaunchNative.SYNCHRONIZE | ProcessNative.ProcessTerminate | ProcessNative.ProcessQueryLimitedInformation,
            false,
            System.Diagnostics.Process.GetCurrentProcess().Id);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Unable to open current process handle for test setup.");

        return handle;
    }

    private sealed class InlineProfileKeeperBootstrapContext : IProfileKeeperBootstrapContext
    {
        public T Run<T>(Func<T> action) => action();
    }
}

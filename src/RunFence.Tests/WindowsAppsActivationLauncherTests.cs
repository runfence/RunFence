using Moq;
using RunFence.Acl;
using RunFence.AppxLauncher;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsActivationLauncherTests
{
    private const string PackageIdentitySourcePath = @"C:\Program Files\WindowsApps\Pkg\App.exe";

    [Fact]
    public void TryLaunch_WindowsAppsTargetCannotBeCreated_Throws()
    {
        var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
        var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>(MockBehavior.Strict);
        var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
        var poller = new FakeWindowsAppsActivationResultPoller();
        var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
        var target = new ProcessLaunchTarget(PackageIdentitySourcePath);
        var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
        var resolvedIdentity = CreateResolvedIdentity(originalIdentity);

        var ex = Assert.Throws<InvalidOperationException>(
            () => launcher.TryLaunch(target, PackageIdentitySourcePath, originalIdentity, resolvedIdentity));

        Assert.Contains("could not be resolved for AppX activation", ex.Message, StringComparison.Ordinal);
        helperLauncher.Verify(
            l => l.Launch(It.IsAny<WindowsAppsActivationTarget>(), It.IsAny<AccountLaunchIdentity>(), It.IsAny<AccountLaunchIdentity>()),
            Times.Never);
    }

    [Fact]
    public void TryLaunch_HelperExitsSuccessfullyWithoutResultFile_ReturnsNullProcess()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity);
            var helperProcess = new FakeWindowsAppsActivationHelperProcess
            {
                HasExited = true,
                ExitCode = 0
            };

            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Callback(() => Directory.CreateDirectory(activationTarget.ResultDirectoryPath))
                .Returns(helperProcess);

            var result = launcher.TryLaunch(
                new ProcessLaunchTarget(PackageIdentitySourcePath),
                PackageIdentitySourcePath,
                originalIdentity,
                resolvedIdentity);

            Assert.Null(result);
            Assert.True(helperProcess.Disposed);
            Assert.False(File.Exists(activationTarget.ResultFilePath));
            Assert.False(Directory.Exists(activationTarget.ResultDirectoryPath));
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryLaunch_HelperExitsWithoutResult_ThrowsWithHelperExitCode()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity);
            var helperProcess = new FakeWindowsAppsActivationHelperProcess
            {
                HasExited = true,
                ExitCode = 23
            };

            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Returns(helperProcess);

            var ex = Assert.Throws<InvalidOperationException>(
                () => launcher.TryLaunch(
                    new ProcessLaunchTarget(PackageIdentitySourcePath),
                    PackageIdentitySourcePath,
                    originalIdentity,
                    resolvedIdentity));

            Assert.Contains("helperExitCode=23", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryLaunch_HelperExitCodeMatchesKnownAppxCode_IncludesEnumName()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity);
            var helperProcess = new FakeWindowsAppsActivationHelperProcess
            {
                HasExited = true,
                ExitCode = (int)AppxLaunchExitCode.ResultFileWriteFailed
            };

            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Returns(helperProcess);

            var ex = Assert.Throws<InvalidOperationException>(
                () => launcher.TryLaunch(
                    new ProcessLaunchTarget(PackageIdentitySourcePath),
                    PackageIdentitySourcePath,
                    originalIdentity,
                    resolvedIdentity));

            Assert.Contains("helperExitCode=7 (ResultFileWriteFailed)", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryLaunch_InvalidResultJson_ThrowsSpecificError()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity);
            var helperProcess = new FakeWindowsAppsActivationHelperProcess();

            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Callback(() =>
                {
                    Directory.CreateDirectory(activationTarget.ResultDirectoryPath);
                    File.WriteAllText(activationTarget.ResultFilePath, "{not json");
                })
                .Returns(helperProcess);

            var ex = Assert.Throws<InvalidOperationException>(
                () => launcher.TryLaunch(
                    new ProcessLaunchTarget(PackageIdentitySourcePath),
                    PackageIdentitySourcePath,
                    originalIdentity,
                    resolvedIdentity));

            Assert.Contains("invalid JSON", ex.Message, StringComparison.Ordinal);
            Assert.True(helperProcess.Disposed);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryLaunch_FailureResultWhileHelperStillRunning_ThrowsFailure()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity);
            var helperProcess = new FakeWindowsAppsActivationHelperProcess();

            poller.OnSleep = _ =>
            {
                Directory.CreateDirectory(activationTarget.ResultDirectoryPath);
                File.WriteAllText(
                    activationTarget.ResultFilePath,
                    """
                    {"ok":false,"stage":"VerifyCreatedProcess","exitCode":13,"hresult":null,"message":"wrong user","appxExecutablePath":"C:\\Program Files\\WindowsApps\\Pkg\\App.exe","arguments":"codex:"}
                    """);
            };
            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Returns(helperProcess);

            var ex = Assert.Throws<InvalidOperationException>(
                () => launcher.TryLaunch(
                    new ProcessLaunchTarget(PackageIdentitySourcePath),
                    PackageIdentitySourcePath,
                    originalIdentity,
                    resolvedIdentity));

            Assert.Contains("stage=VerifyCreatedProcess", ex.Message, StringComparison.Ordinal);
            Assert.Contains("exitCode=13", ex.Message, StringComparison.Ordinal);
            Assert.Contains("wrong user", ex.Message, StringComparison.Ordinal);
            Assert.True(helperProcess.Disposed);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryLaunch_FailureDisposesHelperProcess()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity);
            var helperProcess = new FakeWindowsAppsActivationHelperProcess
            {
                HasExited = true,
                ExitCode = 23
            };

            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Returns(helperProcess);

            Assert.Throws<InvalidOperationException>(
                () => launcher.TryLaunch(
                    new ProcessLaunchTarget(PackageIdentitySourcePath),
                    PackageIdentitySourcePath,
                    originalIdentity,
                    resolvedIdentity));

            Assert.True(helperProcess.Disposed);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void TryLaunch_LowIntegrityIdentity_PreparesLowIntegrityResultDirectoryBeforeLaunch()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var activationTarget = CreateActivationTarget(tempDirectory.FullName);
            var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
            var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>();
            var mandatoryLabelService = new Mock<IMandatoryLabelService>();
            var poller = new FakeWindowsAppsActivationResultPoller();
            var launcher = CreateLauncher(targetFactory.Object, helperLauncher.Object, poller, mandatoryLabelService.Object);
            var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
            var resolvedIdentity = CreateResolvedIdentity(originalIdentity) with { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
            var helperProcess = new FakeWindowsAppsActivationHelperProcess
            {
                HasExited = true,
                ExitCode = 0
            };

            targetFactory
                .Setup(f => f.TryCreate(It.IsAny<ProcessLaunchTarget>(), PackageIdentitySourcePath, resolvedIdentity.Sid))
                .Returns(activationTarget);
            helperLauncher
                .Setup(l => l.Launch(activationTarget, originalIdentity, resolvedIdentity))
                .Callback(() => Assert.True(Directory.Exists(activationTarget.ResultDirectoryPath)))
                .Returns(helperProcess);

            var result = launcher.TryLaunch(
                new ProcessLaunchTarget(PackageIdentitySourcePath),
                PackageIdentitySourcePath,
                originalIdentity,
                resolvedIdentity);

            Assert.Null(result);
            mandatoryLabelService.Verify(s => s.ApplyLowIntegrityLabel(activationTarget.ResultDirectoryPath), Times.Once);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static WindowsAppsActivationLauncher CreateLauncher(
        IWindowsAppsActivationTargetFactory targetFactory,
        IWindowsAppsActivationHelperLauncher helperLauncher,
        IWindowsAppsActivationResultPoller poller,
        IMandatoryLabelService mandatoryLabelService)
    {
        return new WindowsAppsActivationLauncher(targetFactory, helperLauncher, poller, mandatoryLabelService);
    }

    private static AccountLaunchIdentity CreateResolvedIdentity(AccountLaunchIdentity identity)
    {
        return identity with
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess)
        };
    }

    private static WindowsAppsActivationTarget CreateActivationTarget(string parentDirectory)
    {
        var resultDirectory = Path.Combine(parentDirectory, "result");
        return new WindowsAppsActivationTarget(
            new ProcessLaunchTarget(@"C:\RunFence\RunFence.AppxLauncher.exe", "args", HideWindow: true),
            resultDirectory,
            Path.Combine(resultDirectory, "appx-result.jsonl"),
            @"C:\Program Files\WindowsApps\Pkg\App.exe",
            "codex:");
    }

    private sealed class FakeWindowsAppsActivationResultPoller : IWindowsAppsActivationResultPoller
    {
        private DateTime _utcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Action<TimeSpan>? OnSleep { get; set; }

        public DateTime UtcNow => _utcNow;

        public void Sleep(TimeSpan interval)
        {
            OnSleep?.Invoke(interval);
            _utcNow = _utcNow.Add(interval);
        }
    }

    private sealed class FakeWindowsAppsActivationHelperProcess : IWindowsAppsActivationHelperProcess
    {
        public bool HasExited { get; set; }
        public int ExitCode { get; set; }
        public bool Disposed { get; private set; }

        public bool WaitForExit(int timeoutMs) => HasExited;

        public void Dispose()
        {
            Disposed = true;
        }
    }
}

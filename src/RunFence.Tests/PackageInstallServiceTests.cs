using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class PackageInstallServiceTests
{
    private const string TestSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";

    [Fact]
    public async Task InstallPackages_SameSidConcurrentCall_RejectsDuplicateBeforeSecondLaunchAndKeepsOriginalReservationWaitable()
    {
        var launchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLaunch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new FakeInstallProcess();
        var scriptStore = new FakePackageInstallScriptStore();
        var launcher = new BlockingPackageInstallLauncher(
            launchStarted,
            releaseLaunch,
            () => new PackageInstallLaunchResult(process, []));
        var service = CreateService(launcher, scriptStore);
        var package = new InstallablePackage("TestPkg", "Write-Host 'test'");
        var identity = new AccountLaunchIdentity(TestSid);

        var firstInstallTask = Task.Run(() => service.InstallPackagesAsync([package], identity, CancellationToken.None));
        await launchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var waitTask = service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));
        Assert.False(waitTask.IsCompleted);

        var duplicateInstall = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.Run(() => service.InstallPackagesAsync([package], identity, CancellationToken.None)));
        Assert.Contains("already running", duplicateInstall.Message, StringComparison.Ordinal);

        process.SetExited(0);
        releaseLaunch.TrySetResult();

        await firstInstallTask.WaitAsync(TimeSpan.FromSeconds(5));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(scriptStore.CreatedPaths);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);
    }

    [Fact]
    public async Task InstallPackages_LaunchThrows_DeletesScriptAndRemovesReservationForRetry()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var successfulProcess = new FakeInstallProcess(hasExited: true, exitCode: 0);
        var launcher = new SequencePackageInstallLauncher(
            _ => throw new InvalidOperationException("launch failed"),
            _ => new PackageInstallLaunchResult(successfulProcess, []));
        var service = CreateService(launcher, scriptStore);
        var package = new InstallablePackage("TestPkg", "Write-Host 'test'");
        var identity = new AccountLaunchIdentity(TestSid);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InstallPackagesAsync([package], identity, CancellationToken.None));
        Assert.Equal("launch failed", ex.Message);
        Assert.Equal(new[] { scriptStore.CreatedPaths[0] }, scriptStore.DeletedPaths);

        await service.InstallPackagesAsync([package], identity, CancellationToken.None);
        await service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));

        Assert.Equal(2, scriptStore.CreatedPaths.Count);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);
    }

    [Fact]
    public async Task WaitForInstallCompletionAsync_WhenProcessExitsNonZero_ThrowsExistingFailureMessage()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var process = new FakeInstallProcess();
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(process, []));
        var service = CreateService(launcher, scriptStore);
        var package = new InstallablePackage("TestPkg", "Write-Host 'test'");
        var identity = new AccountLaunchIdentity(TestSid);

        await service.InstallPackagesAsync([package], identity, CancellationToken.None);

        var waitTask = service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));
        process.SetExited(23);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => waitTask);
        Assert.Contains("exit code 23", ex.Message, StringComparison.Ordinal);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);
    }

    [Fact]
    public async Task InstallPackages_SinglePackageWithoutDependencies_GeneratesWrappedScriptAndRemovesScriptOnCompletion()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var process = new FakeInstallProcess(hasExited: true, exitCode: 0);
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(process, []));
        var service = CreateService(launcher, scriptStore);
        var package = new InstallablePackage("Winget", "winget install Microsoft.Winget");
        var identity = new AccountLaunchIdentity(TestSid);

        var warnings = await service.InstallPackagesAsync([package], identity, CancellationToken.None);
        await service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));

        Assert.Empty(warnings);
        Assert.Single(scriptStore.CreatedScripts);
        Assert.Single(scriptStore.DeletedPaths);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);

        var createdScript = Assert.Single(scriptStore.CreatedScripts);
        Assert.Equal(TestSid, createdScript.UserSid);
        Assert.Equal(
            $"try {{\n{package.PowerShellCommand}\n}} finally {{\n" +
            "Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue\n}",
            createdScript.Command);
    }

    [Fact]
    public async Task InstallPackages_WithDependencies_GeneratesOrderedCommandsWithoutUnrelatedPackages()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var process = new FakeInstallProcess(hasExited: true, exitCode: 0);
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(process, []));
        var service = CreateService(launcher, scriptStore);
        var dependency = new InstallablePackage("dep", "winget install Example.Dep");
        var requested = new InstallablePackage("pkg", "winget install Example.Pkg", RequiredPackages: [dependency]);
        var unrelated = new InstallablePackage("other", "winget install Example.Other");
        var identity = new AccountLaunchIdentity(TestSid);

        await service.InstallPackagesAsync([requested], identity, CancellationToken.None);
        await service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));

        var command = Assert.Single(scriptStore.CreatedScripts).Command;

        Assert.Contains(dependency.PowerShellCommand, command);
        Assert.Contains(requested.PowerShellCommand, command);
        Assert.DoesNotContain(unrelated.PowerShellCommand, command);

        var dependencyIndex = command.IndexOf(dependency.PowerShellCommand, StringComparison.Ordinal);
        var requestedIndex = command.IndexOf(requested.PowerShellCommand, StringComparison.Ordinal);
        Assert.True(dependencyIndex >= 0 && requestedIndex > dependencyIndex);
        Assert.Equal(1, CountOccurrences(command, dependency.PowerShellCommand));
        Assert.Equal(1, CountOccurrences(command, requested.PowerShellCommand));

        Assert.Equal(
            $"try {{\n{dependency.PowerShellCommand}\n{requested.PowerShellCommand}\n}} finally {{\n" +
            "Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue\n}",
            command);
    }

    [Fact]
    public async Task InstallPackages_WindowsTerminalRequest_EnsuresSharedDeploymentBeforeLaunching()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var process = new FakeInstallProcess(hasExited: true, exitCode: 0);
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(process, []));
        var deploymentService = new Mock<IWindowsTerminalDeploymentService>();
        deploymentService
            .Setup(x => x.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new PackageInstallService(
            launcher,
            scriptStore,
            new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
            Mock.Of<IWindowsTerminalAccountStateService>(),
            deploymentService.Object);

        await service.InstallPackagesAsync([KnownPackages.WindowsTerminal], new AccountLaunchIdentity(TestSid), CancellationToken.None);

        deploymentService.Verify(x => x.EnsureSharedDeploymentReadyAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task InstallPackages_WindowsTerminalDeploymentInProgress_RejectsSameSidRetryBeforeScriptCreation()
    {
        var deploymentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDeployment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scriptStore = new FakePackageInstallScriptStore();
        var process = new FakeInstallProcess(hasExited: true, exitCode: 0);
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(process, []));
        var deploymentCalls = 0;
        var deploymentService = new Mock<IWindowsTerminalDeploymentService>();
        deploymentService
            .Setup(x => x.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (System.Threading.Interlocked.Increment(ref deploymentCalls) != 1)
                    throw new InvalidOperationException("Second deployment should not start.");

                deploymentStarted.TrySetResult();
                releaseDeployment.Task.GetAwaiter().GetResult();
            })
            .Returns(Task.CompletedTask);
        var service = CreateService(launcher, scriptStore, deploymentService.Object);
        var identity = new AccountLaunchIdentity(TestSid);

        var firstInstallTask = Task.Run(() => service.InstallPackagesAsync([KnownPackages.WindowsTerminal], identity, CancellationToken.None));
        await deploymentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var duplicateInstall = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.Run(() => service.InstallPackagesAsync([KnownPackages.WindowsTerminal], identity, CancellationToken.None)));

        Assert.Contains("already running", duplicateInstall.Message, StringComparison.Ordinal);
        Assert.Empty(scriptStore.CreatedPaths);

        releaseDeployment.TrySetResult();
        await firstInstallTask.WaitAsync(TimeSpan.FromSeconds(5));
        await service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));

        Assert.Single(scriptStore.CreatedPaths);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);
    }

    [Fact]
    public async Task InstallPackages_WindowsTerminalDeploymentThrows_RemovesReservationForRetry()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var process = new FakeInstallProcess(hasExited: true, exitCode: 0);
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(process, []));
        var deploymentCalls = 0;
        var deploymentService = new Mock<IWindowsTerminalDeploymentService>();
        deploymentService
            .Setup(x => x.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                deploymentCalls++;
                if (deploymentCalls == 1)
                    throw new InvalidOperationException("deployment failed");
            })
            .Returns(Task.CompletedTask);
        var service = CreateService(launcher, scriptStore, deploymentService.Object);
        var identity = new AccountLaunchIdentity(TestSid);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InstallPackagesAsync([KnownPackages.WindowsTerminal], identity, CancellationToken.None));

        Assert.Equal("deployment failed", exception.Message);
        Assert.Empty(scriptStore.CreatedPaths);

        await service.InstallPackagesAsync([KnownPackages.WindowsTerminal], identity, CancellationToken.None);
        await service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));

        Assert.Equal(2, deploymentCalls);
        Assert.Single(scriptStore.CreatedPaths);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);
    }

    private static PackageInstallService CreateService(
        IPackageInstallLauncher launcher,
        IPackageInstallScriptStore scriptStore,
        IWindowsTerminalDeploymentService? windowsTerminalDeploymentService = null)
    {
        return new PackageInstallService(
            launcher,
            scriptStore,
            new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
            Mock.Of<IWindowsTerminalAccountStateService>(),
            windowsTerminalDeploymentService ?? Mock.Of<IWindowsTerminalDeploymentService>());
    }

    private sealed class FakePackageInstallScriptStore : IPackageInstallScriptStore
    {
        private int _nextId = 1;

        public List<string> CreatedPaths { get; } = [];
        public List<string> DeletedPaths { get; } = [];
        public List<(string Command, string UserSid)> CreatedScripts { get; } = [];
        public int CleanupCalls { get; private set; }

        public string CreateScript(string command, string userSid)
        {
            var path = $@"C:\fake\install-{_nextId++:D4}.ps1";
            CreatedPaths.Add(path);
            CreatedScripts.Add((command, userSid));
            return path;
        }

        public void Delete(string path)
        {
            DeletedPaths.Add(path);
        }

        public void CleanupStaleScripts()
        {
            CleanupCalls++;
        }
    }

    private sealed class BlockingPackageInstallLauncher(
        TaskCompletionSource launchStarted,
        TaskCompletionSource releaseLaunch,
        Func<PackageInstallLaunchResult> createResult)
        : IPackageInstallLauncher
    {
        public PackageInstallLaunchResult Launch(string scriptPath, AccountLaunchIdentity identity)
        {
            launchStarted.TrySetResult();
            releaseLaunch.Task.GetAwaiter().GetResult();
            return createResult();
        }
    }

    private sealed class SequencePackageInstallLauncher(
        params Func<(string ScriptPath, AccountLaunchIdentity Identity), PackageInstallLaunchResult>[] sequence)
        : IPackageInstallLauncher
    {
        private readonly Queue<Func<(string ScriptPath, AccountLaunchIdentity Identity), PackageInstallLaunchResult>> _sequence =
            new(sequence);

        public PackageInstallLaunchResult Launch(string scriptPath, AccountLaunchIdentity identity)
        {
            if (_sequence.Count == 0)
                throw new InvalidOperationException("No launch behavior configured.");

            return _sequence.Dequeue()((scriptPath, identity));
        }
    }

    private sealed class FakeInstallProcess(bool hasExited = false, int exitCode = 0) : IInstallProcess
    {
        public bool HasExited { get; private set; } = hasExited;
        public int ExitCode { get; private set; } = exitCode;
        public bool Disposed { get; private set; }

        public void SetExited(int exitCode)
        {
            ExitCode = exitCode;
            HasExited = true;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = source.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
                break;

            count++;
            index += value.Length;
        }

        return count;
    }
}

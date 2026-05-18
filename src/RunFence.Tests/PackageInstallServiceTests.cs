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

        var firstInstallTask = Task.Run(() => service.InstallPackages([package], identity));
        await launchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var waitTask = service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));
        Assert.False(waitTask.IsCompleted);

        var duplicateInstall = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.Run(() => service.InstallPackages([package], identity)));
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

        var ex = Assert.Throws<InvalidOperationException>(() => service.InstallPackages([package], identity));
        Assert.Equal("launch failed", ex.Message);
        Assert.Equal(new[] { scriptStore.CreatedPaths[0] }, scriptStore.DeletedPaths);

        service.InstallPackages([package], identity);
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

        service.InstallPackages([package], identity);

        var waitTask = service.WaitForInstallCompletionAsync(TestSid, TimeSpan.FromSeconds(10));
        process.SetExited(23);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => waitTask);
        Assert.Contains("exit code 23", ex.Message, StringComparison.Ordinal);
        Assert.Equal(scriptStore.CreatedPaths, scriptStore.DeletedPaths);
    }

    [Fact]
    public void InstallPackages_ReturnsMaintenanceWarningsFromLauncher()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var launcher = new SequencePackageInstallLauncher(
            _ => new PackageInstallLaunchResult(new FakeInstallProcess(hasExited: true, exitCode: 0), ["warning-a", "warning-b"]));
        var service = CreateService(launcher, scriptStore);

        var warnings = service.InstallPackages([new InstallablePackage("TestPkg", "Write-Host 'test'")], new AccountLaunchIdentity(TestSid));

        Assert.Equal(["warning-a", "warning-b"], warnings);
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

        var warnings = service.InstallPackages([package], identity);
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

        service.InstallPackages([requested], identity);
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
    public void CleanupStaleScripts_DelegatesToScriptStore()
    {
        var scriptStore = new FakePackageInstallScriptStore();
        var service = CreateService(new SequencePackageInstallLauncher(), scriptStore);

        service.CleanupStaleScripts();

        Assert.Equal(1, scriptStore.CleanupCalls);
    }

    private static PackageInstallService CreateService(
        IPackageInstallLauncher launcher,
        IPackageInstallScriptStore scriptStore)
    {
        return new PackageInstallService(
            launcher,
            scriptStore,
            new AccountToolResolver(Mock.Of<IProfilePathResolver>()));
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

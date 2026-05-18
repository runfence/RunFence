using System.Text;
using RunFence.Core;
using RunFence.JobKeeper;
using RunFence.Launching.Environment;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperChildProcessLauncherTests
{
    private const int Win32ErrorElevationRequired = 740;

    [Fact]
    public void Launch_UsesFreshEnvironmentSnapshotForEveryRequest()
    {
        var snapshotReader = new TestEnvironmentSnapshotReader(
            new Dictionary<string, string> { ["PATH"] = @"C:\First" },
            new Dictionary<string, string> { ["PATH"] = @"C:\Second" });
        var executableResolver = new CapturingExecutablePathResolver();
        var environmentBlockFactory = new CapturingEnvironmentBlockFactory();
        var nativeApi = new CapturingNativeProcessApi();
        var registry = new CapturingChildProcessRegistry();
        var launcher = CreateLauncher(executableResolver, snapshotReader, environmentBlockFactory, nativeApi, registry);

        launcher.Launch(new JobKeeperLaunchRequest("app.exe", null, null, false, false, null));
        launcher.Launch(new JobKeeperLaunchRequest("app.exe", null, null, false, false, null));

        Assert.Equal(2, snapshotReader.ReadCount);
        Assert.Equal(@"C:\First", executableResolver.Environments[0]["PATH"]);
        Assert.Equal(@"C:\Second", executableResolver.Environments[1]["PATH"]);
        Assert.Equal(@"C:\First", environmentBlockFactory.Environments[0]["PATH"]);
        Assert.Equal(@"C:\Second", environmentBlockFactory.Environments[1]["PATH"]);
        Assert.Equal(2, environmentBlockFactory.DisposeCount);
        Assert.Equal([new IntPtr(10), new IntPtr(10)], registry.RegisteredHandles);
    }

    [Fact]
    public void Launch_UsesSameEffectiveEnvironmentForResolutionAndCreatedProcess()
    {
        var snapshotReader = new TestEnvironmentSnapshotReader(new Dictionary<string, string>
        {
            ["PATH"] = @"C:\Base",
            ["BASE_ONLY"] = "1",
        });
        var executableResolver = new CapturingExecutablePathResolver();
        var environmentBlockFactory = new CapturingEnvironmentBlockFactory();
        var nativeApi = new CapturingNativeProcessApi();
        var registry = new CapturingChildProcessRegistry();
        var launcher = CreateLauncher(executableResolver, snapshotReader, environmentBlockFactory, nativeApi, registry);
        var request = new JobKeeperLaunchRequest(
            "app.exe",
            "--flag",
            @"C:\Work",
            HideWindow: true,
            SuppressStartupFeedback: true,
            new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Override",
                ["EXTRA"] = "2",
            });

        var response = launcher.Launch(request);

        Assert.Equal(1234, response.Pid);
        Assert.Equal(0, response.Error);
        Assert.Equal(@"C:\Override", executableResolver.Environments.Single()["PATH"]);
        Assert.Equal("1", executableResolver.Environments.Single()["BASE_ONLY"]);
        Assert.Equal("2", executableResolver.Environments.Single()["EXTRA"]);
        Assert.Equal(executableResolver.Environments.Single(), environmentBlockFactory.Environments.Single());
        Assert.Equal(environmentBlockFactory.BlockPointer, nativeApi.EnvironmentBlock);
        Assert.True(nativeApi.CreateUnicodeEnvironmentFlagSet);
        Assert.Equal(@"""C:\Resolved\App.exe"" --flag", nativeApi.CommandLine);
        Assert.Equal(@"C:\Resolved\App.exe", nativeApi.ApplicationName);
        Assert.Equal(@"C:\Work", nativeApi.WorkingDirectory);
        Assert.True(nativeApi.HideWindow);
        Assert.True(nativeApi.SuppressStartupFeedback);
        Assert.Equal(1, environmentBlockFactory.DisposeCount);
        Assert.Equal([new IntPtr(10)], registry.RegisteredHandles);
        Assert.Equal([new IntPtr(11)], nativeApi.ClosedHandles);
    }

    [Fact]
    public void Launch_PassesSuppressStartupFeedbackToNativeCreateProcess()
    {
        var nativeApi = new CapturingNativeProcessApi();
        var launcher = CreateLauncher(
            new CapturingExecutablePathResolver(),
            new TestEnvironmentSnapshotReader(new Dictionary<string, string>()),
            new CapturingEnvironmentBlockFactory(),
            nativeApi,
            new CapturingChildProcessRegistry());

        launcher.Launch(new JobKeeperLaunchRequest("app.exe", null, null, false, true, null));

        Assert.True(nativeApi.SuppressStartupFeedback);
    }

    [Fact]
    public void Launch_RootedResolvedExecutable_PassesApplicationNameToNativeCreateProcess()
    {
        var nativeApi = new CapturingNativeProcessApi();
        var registry = new CapturingChildProcessRegistry();
        var launcher = CreateLauncher(
            new CapturingExecutablePathResolver(@"C:\Windows\notepad.exe"),
            new TestEnvironmentSnapshotReader(new Dictionary<string, string>()),
            new CapturingEnvironmentBlockFactory(),
            nativeApi,
            registry);

        launcher.Launch(new JobKeeperLaunchRequest(@"C:\Windows\notepad.exe", null, null, false, false, null));

        Assert.Equal(@"C:\Windows\notepad.exe", nativeApi.ApplicationName);
        Assert.Equal(@"""C:\Windows\notepad.exe""", nativeApi.CommandLine);
    }

    [Fact]
    public void Launch_RelativeUnresolvedExecutable_DoesNotPassApplicationName()
    {
        var nativeApi = new CapturingNativeProcessApi();
        var registry = new CapturingChildProcessRegistry();
        var launcher = CreateLauncher(
            new CapturingExecutablePathResolver("tool.exe"),
            new TestEnvironmentSnapshotReader(new Dictionary<string, string>()),
            new CapturingEnvironmentBlockFactory(),
            nativeApi,
            registry);

        launcher.Launch(new JobKeeperLaunchRequest("tool.exe", null, null, false, false, null));

        Assert.Null(nativeApi.ApplicationName);
        Assert.Equal(@"""tool.exe""", nativeApi.CommandLine);
    }

    [Fact]
    public void Launch_AccessDenied_ReturnsErrorWithoutRepairing()
    {
        var packageExe = NotepadPackageExe();
        var environmentBlockFactory = new CapturingEnvironmentBlockFactory();
        var nativeApi = new CapturingNativeProcessApi(failureErrors: [5]);
        var registry = new CapturingChildProcessRegistry();
        var launcher = new JobKeeperChildProcessLauncher(
            new CapturingExecutablePathResolver(packageExe),
            new TestEnvironmentSnapshotReader(new Dictionary<string, string>()),
            environmentBlockFactory,
            nativeApi,
            registry);

        var response = launcher.Launch(new JobKeeperLaunchRequest(packageExe, null, null, false, false, null));

        Assert.Equal(0, response.Pid);
        Assert.Equal(5, response.Error);
        Assert.Equal(1, environmentBlockFactory.DisposeCount);
        Assert.Empty(registry.RegisteredHandles);
        Assert.Empty(nativeApi.ClosedHandles);
    }

    [Fact]
    public void Launch_ElevationRequired_RetriesWithRunAsInvoker()
    {
        var environmentBlockFactory = new CapturingEnvironmentBlockFactory();
        var nativeApi = new CapturingNativeProcessApi(
            failureErrors:
            [
                Win32ErrorElevationRequired
            ]);
        var registry = new CapturingChildProcessRegistry();
        var launcher = CreateLauncher(
            new CapturingExecutablePathResolver(),
            new TestEnvironmentSnapshotReader(new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base"
            }),
            environmentBlockFactory,
            nativeApi,
            registry);

        var response = launcher.Launch(new JobKeeperLaunchRequest("app.exe", null, null, false, false, null));

        Assert.Equal(1234, response.Pid);
        Assert.Equal(0, response.Error);
        Assert.Equal(2, nativeApi.CreateProcessCallCount);
        Assert.Equal(2, environmentBlockFactory.Environments.Count);
        Assert.False(environmentBlockFactory.Environments[0].ContainsKey("__COMPAT_LAYER"));
        Assert.Equal("RunAsInvoker", environmentBlockFactory.Environments[1]["__COMPAT_LAYER"]);
        Assert.Equal([new IntPtr(10)], registry.RegisteredHandles);
        Assert.Equal([new IntPtr(11)], nativeApi.ClosedHandles);
    }

    [Fact]
    public void Launch_ElevationRequiredWithExistingCompatLayer_DoesNotRetry()
    {
        var environmentBlockFactory = new CapturingEnvironmentBlockFactory();
        var nativeApi = new CapturingNativeProcessApi(
            failureErrors:
            [
                Win32ErrorElevationRequired
            ]);
        var registry = new CapturingChildProcessRegistry();
        var launcher = CreateLauncher(
            new CapturingExecutablePathResolver(),
            new TestEnvironmentSnapshotReader(new Dictionary<string, string>()),
            environmentBlockFactory,
            nativeApi,
            registry);

        var response = launcher.Launch(new JobKeeperLaunchRequest(
            "app.exe",
            null,
            null,
            false,
            false,
            new Dictionary<string, string>
            {
                ["__COMPAT_LAYER"] = "ExistingValue"
            }));

        Assert.Equal(0, response.Pid);
        Assert.Equal(Win32ErrorElevationRequired, response.Error);
        Assert.Equal(1, nativeApi.CreateProcessCallCount);
        Assert.Single(environmentBlockFactory.Environments);
        Assert.Equal("ExistingValue", environmentBlockFactory.Environments[0]["__COMPAT_LAYER"]);
        Assert.Empty(registry.RegisteredHandles);
        Assert.Empty(nativeApi.ClosedHandles);
    }

    [Fact]
    public void WindowsAppsPackagePathParser_ParsesPackageFamilyName()
    {
        Assert.True(WindowsAppsPackagePathParser.TryParsePackagePath(NotepadPackageExe(), out var packagePath));
        Assert.Equal("Microsoft.WindowsNotepad", packagePath.PackageName);
        Assert.Equal(new Version("11.2512.29.0"), packagePath.Version);
        Assert.Equal("x64", packagePath.Architecture);
        Assert.Equal("8wekyb3d8bbwe", packagePath.PublisherId);
        Assert.Equal(@"Notepad\Notepad.exe", packagePath.RelativeExecutablePath);
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe", packagePath.PackageFamilyName);
    }

    private static JobKeeperChildProcessLauncher CreateLauncher(
        IJobKeeperExecutablePathResolver executableResolver,
        IJobKeeperEnvironmentSnapshotReader snapshotReader,
        IJobKeeperEnvironmentBlockFactory environmentBlockFactory,
        IJobKeeperNativeProcessApi nativeApi,
        IJobKeeperChildProcessRegistry childProcessRegistry)
        => new(
            executableResolver,
            snapshotReader,
            environmentBlockFactory,
            nativeApi,
            childProcessRegistry);

    private static string NotepadPackageExe() =>
        Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");

    private sealed class TestEnvironmentSnapshotReader(params Dictionary<string, string>[] snapshots)
        : IJobKeeperEnvironmentSnapshotReader
    {
        private int _index;

        public int ReadCount { get; private set; }

        public Dictionary<string, string> ReadAll()
        {
            ReadCount++;
            var snapshot = snapshots[Math.Min(_index, snapshots.Length - 1)];
            _index++;
            return new Dictionary<string, string>(snapshot, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class CapturingExecutablePathResolver(string resolvedPath = @"C:\Resolved\App.exe")
        : IJobKeeperExecutablePathResolver
    {
        public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];

        public string Resolve(string exePath, IReadOnlyDictionary<string, string> environment)
        {
            Environments.Add(new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase));
            return resolvedPath;
        }
    }

    private sealed class CapturingEnvironmentBlockFactory : IJobKeeperEnvironmentBlockFactory
    {
        public IntPtr BlockPointer { get; } = new(99);
        public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];
        public int DisposeCount { get; private set; }

        public EnvironmentBlock Build(IReadOnlyDictionary<string, string> environment)
        {
            Environments.Add(new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase));
            return EnvironmentBlock.Own(BlockPointer, environmentBlock =>
            {
                Assert.Equal(BlockPointer, environmentBlock);
                DisposeCount++;
            });
        }
    }

    private sealed class CapturingNativeProcessApi : IJobKeeperNativeProcessApi
    {
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private readonly Queue<int> _failureErrors;

        public CapturingNativeProcessApi(IEnumerable<int>? failureErrors = null)
        {
            _failureErrors = new Queue<int>(failureErrors ?? []);
        }

        public int CreateProcessCallCount { get; private set; }
        public string? ApplicationName { get; private set; }
        public List<string?> ApplicationNames { get; } = [];
        public string? CommandLine { get; private set; }
        public IntPtr EnvironmentBlock { get; private set; }
        public string? WorkingDirectory { get; private set; }
        public bool HideWindow { get; private set; }
        public bool SuppressStartupFeedback { get; private set; }
        public bool CreateUnicodeEnvironmentFlagSet { get; private set; }
        public int LastError { get; private set; }
        public List<IntPtr> ClosedHandles { get; } = [];

        public bool CreateProcess(
            string? applicationName,
            StringBuilder commandLine,
            uint creationFlags,
            IntPtr environmentBlock,
            string? workingDirectory,
            bool hideWindow,
            bool suppressStartupFeedback,
            out JobKeeperProcessInformation processInformation)
        {
            CreateProcessCallCount++;
            ApplicationName = applicationName;
            ApplicationNames.Add(applicationName);
            CommandLine = commandLine.ToString();
            EnvironmentBlock = environmentBlock;
            WorkingDirectory = workingDirectory;
            HideWindow = hideWindow;
            SuppressStartupFeedback = suppressStartupFeedback;
            CreateUnicodeEnvironmentFlagSet = (creationFlags & CreateUnicodeEnvironment) != 0;
            if (_failureErrors.Count > 0)
            {
                LastError = _failureErrors.Dequeue();
                processInformation = default;
                return false;
            }

            processInformation = new JobKeeperProcessInformation(new IntPtr(10), new IntPtr(11), 1234, 5678);
            return true;
        }

        public void CloseHandle(IntPtr handle)
        {
            ClosedHandles.Add(handle);
        }

        public int GetLastWin32Error() => LastError;

        public bool TerminateProcess(IntPtr processHandle, uint exitCode) => true;

        public string? TryGetProcessImagePath(IntPtr processHandle) => null;

        public bool WaitForProcessExit(IntPtr processHandle, uint timeoutMilliseconds) => false;
    }

    private sealed class CapturingChildProcessRegistry : IJobKeeperChildProcessRegistry
    {
        public List<IntPtr> RegisteredHandles { get; } = [];

        public void Register(IntPtr processHandle)
        {
            RegisteredHandles.Add(processHandle);
        }

        public int PruneExitedAndCountActive() => RegisteredHandles.Count;

        public bool TryExitAfterCleaningIgnoredProcesses() => RegisteredHandles.Count == 0;

    }
}

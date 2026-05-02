using System.Text;
using RunFence.Core;
using RunFence.JobKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperChildProcessLauncherTests
{
    [Fact]
    public void Launch_UsesFreshEnvironmentSnapshotForEveryRequest()
    {
        var snapshotReader = new TestEnvironmentSnapshotReader(
            new Dictionary<string, string> { ["PATH"] = @"C:\First" },
            new Dictionary<string, string> { ["PATH"] = @"C:\Second" });
        var executableResolver = new CapturingExecutablePathResolver();
        var environmentBlockFactory = new CapturingEnvironmentBlockFactory();
        var nativeApi = new CapturingNativeProcessApi();
        var launcher = new JobKeeperChildProcessLauncher(
            executableResolver,
            snapshotReader,
            environmentBlockFactory,
            nativeApi);

        launcher.Launch(new JobKeeperLaunchRequest("app.exe", null, null, false, null));
        launcher.Launch(new JobKeeperLaunchRequest("app.exe", null, null, false, null));

        Assert.Equal(2, snapshotReader.ReadCount);
        Assert.Equal(@"C:\First", executableResolver.Environments[0]["PATH"]);
        Assert.Equal(@"C:\Second", executableResolver.Environments[1]["PATH"]);
        Assert.Equal(@"C:\First", environmentBlockFactory.Environments[0]["PATH"]);
        Assert.Equal(@"C:\Second", environmentBlockFactory.Environments[1]["PATH"]);
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
        var launcher = new JobKeeperChildProcessLauncher(
            executableResolver,
            snapshotReader,
            environmentBlockFactory,
            nativeApi);
        var request = new JobKeeperLaunchRequest(
            "app.exe",
            "--flag",
            @"C:\Work",
            HideWindow: true,
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
        Assert.Equal(@"C:\Work", nativeApi.WorkingDirectory);
        Assert.True(nativeApi.HideWindow);
        Assert.True(environmentBlockFactory.Freed);
    }

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

    private sealed class CapturingExecutablePathResolver : IJobKeeperExecutablePathResolver
    {
        public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];

        public string Resolve(string exePath, IReadOnlyDictionary<string, string> environment)
        {
            Environments.Add(new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase));
            return @"C:\Resolved\App.exe";
        }
    }

    private sealed class CapturingEnvironmentBlockFactory : IJobKeeperEnvironmentBlockFactory
    {
        public IntPtr BlockPointer { get; } = new(99);
        public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];
        public bool Freed { get; private set; }

        public IntPtr Build(IReadOnlyDictionary<string, string> environment)
        {
            Environments.Add(new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase));
            return BlockPointer;
        }

        public void Free(IntPtr environmentBlock)
        {
            Assert.Equal(BlockPointer, environmentBlock);
            Freed = true;
        }
    }

    private sealed class CapturingNativeProcessApi : IJobKeeperNativeProcessApi
    {
        private const uint CreateUnicodeEnvironment = 0x00000400;

        public string? CommandLine { get; private set; }
        public IntPtr EnvironmentBlock { get; private set; }
        public string? WorkingDirectory { get; private set; }
        public bool HideWindow { get; private set; }
        public bool CreateUnicodeEnvironmentFlagSet { get; private set; }

        public bool CreateProcess(
            StringBuilder commandLine,
            uint creationFlags,
            IntPtr environmentBlock,
            string? workingDirectory,
            bool hideWindow,
            out JobKeeperProcessInformation processInformation)
        {
            CommandLine = commandLine.ToString();
            EnvironmentBlock = environmentBlock;
            WorkingDirectory = workingDirectory;
            HideWindow = hideWindow;
            CreateUnicodeEnvironmentFlagSet = (creationFlags & CreateUnicodeEnvironment) != 0;
            processInformation = new JobKeeperProcessInformation(new IntPtr(10), new IntPtr(11), 1234, 5678);
            return true;
        }

        public void CloseHandle(IntPtr handle)
        {
        }

        public int GetLastWin32Error() => 0;
    }
}

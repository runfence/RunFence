using System.ComponentModel;
using System.Runtime.InteropServices;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Environment;
using RunFence.Launch.Tokens;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class PreparedTokenProcessLauncherTests
{
    [Fact]
    public void LaunchWithPreparedToken_ResolvesPath_AndRetriesWithoutBreakaway()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        ExecutablePathResolutionContext? capturedContext = null;
        resolver.Setup(r => r.TryResolvePath("wt.exe", It.IsAny<ExecutablePathResolutionContext>()))
            .Callback<string, ExecutablePathResolutionContext>((_, context) => capturedContext = context)
            .Returns(@"C:\Resolved\wt.exe");

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(5),
                new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 42 }
            ]);

        var result = launcher.LaunchWithPreparedToken(
            new IntPtr(11),
            new ProcessLaunchTarget("wt.exe"),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1001");

        Assert.Equal((uint)42, result.dwProcessId);
        Assert.NotNull(capturedContext);
        Assert.False(capturedContext!.SearchCurrentProcessPath);
        Assert.Null(capturedContext.EnvironmentReader);
        Assert.Equal(2, launcher.Invocations.Count);
        Assert.Equal(@"C:\Resolved\wt.exe", launcher.Invocations[0].Target.ExePath);
        Assert.True(launcher.Invocations[0].Suspended);
        Assert.True(launcher.Invocations[0].BreakawayFromJob);
        Assert.True(launcher.Invocations[1].Suspended);
        Assert.False(launcher.Invocations[1].BreakawayFromJob);
        Assert.Equal(0, launcher.DisablePrivilegesCallCount);
    }

    [Fact]
    public void LaunchWithPreparedToken_CurrentProcessToken_RetriesUnsuspendedAfterSecondAccessDenied()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(5),
                new Win32Exception(5),
                new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 55 }
            ]);

        var result = launcher.LaunchWithPreparedToken(
            new IntPtr(12),
            new ProcessLaunchTarget(@"C:\Apps\app.exe"),
            LaunchTokenSource.CurrentProcess,
            "S-1-5-21-1-2-3-1002");

        Assert.Equal((uint)55, result.dwProcessId);
        Assert.Equal(1, launcher.DisablePrivilegesCallCount);
        Assert.Contains(TokenPrivilegeHelper.SeRelabelPrivilege, launcher.DisabledPrivileges.Single());
        Assert.Contains(TokenPrivilegeHelper.SeTcbPrivilege, launcher.DisabledPrivileges.Single());
        Assert.Equal(3, launcher.Invocations.Count);
        Assert.True(launcher.Invocations[0].Suspended);
        Assert.True(launcher.Invocations[0].BreakawayFromJob);
        Assert.True(launcher.Invocations[1].Suspended);
        Assert.False(launcher.Invocations[1].BreakawayFromJob);
        Assert.False(launcher.Invocations[2].Suspended);
        Assert.False(launcher.Invocations[2].BreakawayFromJob);
    }

    [Fact]
    public void LaunchWithPreparedToken_CurrentProcessToken_DisablesStartupPrivileges()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 56 }]);

        _ = launcher.LaunchWithPreparedToken(
            new IntPtr(120),
            new ProcessLaunchTarget(@"C:\Apps\app.exe"),
            LaunchTokenSource.CurrentProcess,
            "S-1-5-21-1-2-3-1010");

        Assert.Single(launcher.DisabledPrivileges);
        Assert.Contains(TokenPrivilegeHelper.SeRelabelPrivilege, launcher.DisabledPrivileges[0]);
        Assert.Contains(TokenPrivilegeHelper.SeTcbPrivilege, launcher.DisabledPrivileges[0]);
    }

    [Fact]
    public void LaunchWithPreparedToken_AllowUnsuspendedRetryFalse_ThrowsAfterSecondAccessDenied()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(5),
                new Win32Exception(5)
            ]);

        Assert.Throws<Win32Exception>(() => launcher.LaunchWithPreparedToken(
            new IntPtr(13),
            new ProcessLaunchTarget(@"C:\Apps\app.exe"),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1003",
            allowUnsuspendedRetry: false));
        Assert.Equal(2, launcher.Invocations.Count);
    }

    [Fact]
    public void LaunchWithPreparedToken_DisposesPreparedEnvironmentBlock()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new ReleasingPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 42 }],
            new IntPtr(1001));

        launcher.LaunchWithPreparedToken(
            new IntPtr(14),
            new ProcessLaunchTarget(@"C:\Apps\app.exe"),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1004");

        Assert.Equal(1, launcher.ReleaseCount);
    }

    [Fact]
    public void LaunchWithPreparedToken_ElevationRequired_RetriesWithRunAsInvokerEnvironment()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(ProcessLaunchNative.Win32ErrorElevationRequired),
                new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 77 }
            ],
            CreateEnvironmentBlock(new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base",
                ["TEMP"] = @"C:\Temp"
            }));

        var result = launcher.LaunchWithPreparedToken(
            new IntPtr(15),
            new ProcessLaunchTarget(@"C:\Windows\regedit.exe"),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1005");

        Assert.Equal((uint)77, result.dwProcessId);
        Assert.Equal(2, launcher.Invocations.Count);
        Assert.Equal(@"C:\Base", launcher.Invocations[0].EnvironmentVariables["PATH"]);
        Assert.False(launcher.Invocations[0].EnvironmentVariables.ContainsKey("__COMPAT_LAYER"));
        Assert.Equal("RunAsInvoker", launcher.Invocations[1].EnvironmentVariables["__COMPAT_LAYER"]);
        Assert.Equal(@"C:\Base", launcher.Invocations[1].EnvironmentVariables["PATH"]);
        Assert.Equal(@"C:\Temp", launcher.Invocations[1].EnvironmentVariables["TEMP"]);
    }

    [Fact]
    public void LaunchWithPreparedToken_ElevationRequiredWithoutEnvironmentBlock_RetriesWithSyntheticEnvironment()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(ProcessLaunchNative.Win32ErrorElevationRequired),
                new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 88 }
            ],
            currentProcessEnvironmentVariables: new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base"
            });

        var result = launcher.LaunchWithPreparedToken(
            new IntPtr(16),
            new ProcessLaunchTarget(
                @"C:\Windows\regedit.exe",
                EnvironmentVariables: new Dictionary<string, string>
                {
                    ["RF_TEST_OVERRIDE"] = "1"
                }),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1006");

        Assert.Equal((uint)88, result.dwProcessId);
        Assert.Equal(2, launcher.Invocations.Count);
        Assert.Equal("RunAsInvoker", launcher.Invocations[1].EnvironmentVariables["__COMPAT_LAYER"]);
        Assert.Equal(@"C:\Base", launcher.Invocations[1].EnvironmentVariables["PATH"]);
        Assert.Equal("1", launcher.Invocations[1].EnvironmentVariables["RF_TEST_OVERRIDE"]);
    }

    [Fact]
    public void LaunchWithPreparedToken_ElevationRequiredWithExistingCompatLayer_DoesNotRetry()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [new Win32Exception(ProcessLaunchNative.Win32ErrorElevationRequired)]);

        Assert.Throws<Win32Exception>(() => launcher.LaunchWithPreparedToken(
            new IntPtr(17),
            new ProcessLaunchTarget(
                @"C:\Windows\regedit.exe",
                EnvironmentVariables: new Dictionary<string, string>
                {
                    ["__COMPAT_LAYER"] = "ExistingValue"
                }),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1007"));
        Assert.Single(launcher.Invocations);
    }

    [Fact]
    public void LaunchWithPreparedToken_CurrentProcessElevationRequired_RetriesWithRunAsInvoker()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(ProcessLaunchNative.Win32ErrorElevationRequired),
                new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 91 }
            ],
            CreateEnvironmentBlock(new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base"
            }));

        var result = launcher.LaunchWithPreparedToken(
            new IntPtr(18),
            new ProcessLaunchTarget(@"C:\Windows\regedit.exe"),
            LaunchTokenSource.CurrentProcess,
            "S-1-5-21-1-2-3-1008");
        Assert.Equal((uint)91, result.dwProcessId);
        Assert.Equal(2, launcher.Invocations.Count);
        Assert.False(launcher.Invocations[0].EnvironmentVariables.ContainsKey("__COMPAT_LAYER"));
        Assert.Equal("RunAsInvoker", launcher.Invocations[1].EnvironmentVariables["__COMPAT_LAYER"]);
    }

    [Fact]
    public void LaunchWithPreparedToken_ElevationRequiredAfterBreakawayRetry_RetriesWithRunAsInvokerEnvironment()
    {
        var resolver = new Mock<IExecutablePathResolver>();
        resolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string?)null);

        var launcher = new TestPreparedTokenProcessLauncher(
            new Mock<ILoggingService>().Object,
            resolver.Object,
            [
                new Win32Exception(5),
                new Win32Exception(ProcessLaunchNative.Win32ErrorElevationRequired),
                new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 99 }
            ],
            CreateEnvironmentBlock(new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base"
            }));

        var result = launcher.LaunchWithPreparedToken(
            new IntPtr(19),
            new ProcessLaunchTarget(@"C:\Windows\regedit.exe"),
            LaunchTokenSource.Credentials,
            "S-1-5-21-1-2-3-1009");

        Assert.Equal((uint)99, result.dwProcessId);
        Assert.Equal(3, launcher.Invocations.Count);
        Assert.False(launcher.Invocations[1].EnvironmentVariables.ContainsKey("__COMPAT_LAYER"));
        Assert.Equal("RunAsInvoker", launcher.Invocations[2].EnvironmentVariables["__COMPAT_LAYER"]);
        Assert.True(launcher.Invocations[2].Suspended);
        Assert.True(launcher.Invocations[2].BreakawayFromJob);
    }

    private sealed class TestPreparedTokenProcessLauncher : PreparedTokenProcessLauncher
    {
        private readonly Queue<object> _launchOutcomes;
        private readonly EnvironmentBlock _environmentBlock;
        private readonly IReadOnlyDictionary<string, string>? _currentProcessEnvironmentVariables;

        public TestPreparedTokenProcessLauncher(
            ILoggingService log,
            IExecutablePathResolver executablePathResolver,
            IEnumerable<object> launchOutcomes,
            EnvironmentBlock? environmentBlock = null,
            IReadOnlyDictionary<string, string>? currentProcessEnvironmentVariables = null)
            : base(log, executablePathResolver)
        {
            _launchOutcomes = new Queue<object>(launchOutcomes);
            _environmentBlock = environmentBlock ?? EnvironmentBlock.Empty();
            _currentProcessEnvironmentVariables = currentProcessEnvironmentVariables;
        }

        public int DisablePrivilegesCallCount { get; private set; }

        public List<LaunchInvocation> Invocations { get; } = [];

        public List<string[]> DisabledPrivileges { get; } = [];

        protected override void AllowSetForegroundWindowAny()
        {
        }

        protected override void DisablePrivilegesOnToken(IntPtr token, string[] privileges)
        {
            DisablePrivilegesCallCount++;
            DisabledPrivileges.Add(privileges);
        }

        protected override void SetRestrictiveDefaultDacl(IntPtr token, string accountSid)
        {
        }

        protected override bool TryCreateEnvironmentBlock(IntPtr token, out EnvironmentBlock envBlock)
        {
            if (_environmentBlock.Pointer == IntPtr.Zero)
            {
                envBlock = EnvironmentBlock.Empty();
                return false;
            }

            var vars = NativeEnvironmentBlockReader.Read(_environmentBlock.Pointer);
            using var copy = EnvironmentBlock.Build(vars);
            envBlock = EnvironmentBlock.Own(copy.Detach(), Marshal.FreeHGlobal);
            return true;
        }

        protected override Dictionary<string, string> ReadCurrentProcessEnvironment()
            => _currentProcessEnvironmentVariables != null
                ? new Dictionary<string, string>(_currentProcessEnvironmentVariables, StringComparer.OrdinalIgnoreCase)
                : base.ReadCurrentProcessEnvironment();

        protected override ProcessLaunchNative.PROCESS_INFORMATION CreateProcessWithToken(
            IntPtr token,
            ProcessLaunchTarget target,
            IntPtr environmentPointer,
            bool suspended,
            bool breakawayFromJob)
        {
            Invocations.Add(new LaunchInvocation(
                target,
                suspended,
                breakawayFromJob,
                NativeEnvironmentBlockReader.Read(environmentPointer)));
            var next = _launchOutcomes.Dequeue();
            if (next is Exception exception)
                throw exception;

            return (ProcessLaunchNative.PROCESS_INFORMATION)next;
        }
    }

    private sealed class ReleasingPreparedTokenProcessLauncher : PreparedTokenProcessLauncher
    {
        private readonly Queue<ProcessLaunchNative.PROCESS_INFORMATION> _launchOutcomes;
        private readonly IntPtr _environmentPointer;

        public ReleasingPreparedTokenProcessLauncher(
            ILoggingService log,
            IExecutablePathResolver executablePathResolver,
            IEnumerable<ProcessLaunchNative.PROCESS_INFORMATION> launchOutcomes,
            IntPtr environmentPointer)
            : base(log, executablePathResolver)
        {
            _launchOutcomes = new Queue<ProcessLaunchNative.PROCESS_INFORMATION>(launchOutcomes);
            _environmentPointer = environmentPointer;
        }

        public int ReleaseCount { get; private set; }

        protected override bool TryCreateEnvironmentBlock(IntPtr token, out EnvironmentBlock envBlock)
        {
            envBlock = EnvironmentBlock.Own(_environmentPointer, _ => ReleaseCount++);
            return true;
        }

        protected override void SetRestrictiveDefaultDacl(IntPtr token, string accountSid)
        {
        }

        protected override ProcessLaunchNative.PROCESS_INFORMATION CreateProcessWithToken(
            IntPtr token,
            ProcessLaunchTarget target,
            IntPtr environmentPointer,
            bool suspended,
            bool breakawayFromJob)
        {
            return _launchOutcomes.Dequeue();
        }
    }

    private static EnvironmentBlock CreateEnvironmentBlock(IReadOnlyDictionary<string, string> variables)
    {
        using var block = EnvironmentBlock.Build(variables);
        return EnvironmentBlock.Own(block.Detach(), Marshal.FreeHGlobal);
    }

    private sealed record LaunchInvocation(
        ProcessLaunchTarget Target,
        bool Suspended,
        bool BreakawayFromJob,
        IReadOnlyDictionary<string, string> EnvironmentVariables);
}

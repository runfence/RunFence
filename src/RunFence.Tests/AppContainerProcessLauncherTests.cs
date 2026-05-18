using System.ComponentModel;
using System.Runtime.InteropServices;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Launching.Environment;
using Xunit;

namespace RunFence.Tests;

public class AppContainerProcessLauncherTests
{
    [Fact]
    public void LaunchFile_ProfileSetupFailure_AbortsBeforeTokenCreation()
    {
        var profileSetup = new Mock<IAppContainerProfileSetup>();
        profileSetup
            .Setup(s => s.EnsureProfileUnderToken(It.IsAny<AppContainerEntry>(), It.IsAny<IntPtr>()))
            .Returns(AppContainerProfileSetupResult.Failure(
                AppContainerProfileSetupStatus.ProfileFailed,
                "profile setup failed"));
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>(MockBehavior.Strict);

        var launcher = CreateLauncher(
            profileSetup: profileSetup.Object,
            tokenBuilder: tokenBuilder.Object);

        var exception = Assert.Throws<InvalidOperationException>(() => launcher.LaunchFile(
            new ProcessLaunchTarget(@"C:\apps\browser.exe"),
            CreateIdentity()));

        Assert.Contains("profile setup failed", exception.Message, StringComparison.Ordinal);
        tokenBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void LaunchFile_UsesDerivedContainerSidForDataFolderSetup()
    {
        var dataFolderService = new Mock<IAppContainerDataFolderService>();
        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_browser")).Returns("S-1-15-2-42");

        var tokenContext = CreateTokenContext();
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        tokenBuilder
            .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", null))
            .Returns(tokenContext.Context);

        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        environmentSetup
            .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", @"C:\apps\browser.exe"))
            .Returns(CreateEnvironmentBlock(new Dictionary<string, string>
            {
                ["BASE"] = "1"
            }));

        var processStarter = new Mock<IAppContainerProcessStarter>();
        processStarter
            .Setup(s => s.Start(tokenContext.AppContainerToken, It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
            .Throws(new Win32Exception(8, "process creation failed"));

        var launcher = CreateLauncher(
            dataFolderService: dataFolderService.Object,
            sidProvider: sidProvider.Object,
            tokenBuilder: tokenBuilder.Object,
            environmentSetup: environmentSetup.Object,
            processStarter: processStarter.Object);

        Assert.Throws<Win32Exception>(() => launcher.LaunchFile(
            new ProcessLaunchTarget(@"C:\apps\browser.exe"),
            CreateIdentity()));

        dataFolderService.Verify(s => s.EnsureContainerDataFolder(It.IsAny<AppContainerEntry>(), "S-1-15-2-42"), Times.Once);
        dataFolderService.Verify(s => s.EnsureDataFolderTraverse(It.IsAny<AppContainerEntry>(), "S-1-15-2-42"), Times.Once);
    }

    [Fact]
    public void LaunchFile_Success_UsesCollaboratorsInOrder_MergesEnvironment_AndFallsBackWorkingDirectory()
    {
        var sequence = new MockSequence();
        var profileSetup = new Mock<IAppContainerProfileSetup>();
        var dataFolderService = new Mock<IAppContainerDataFolderService>();
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        var processStarter = new Mock<IAppContainerProcessStarter>();
        var tokenContext = CreateTokenContext();
        var target = new ProcessLaunchTarget(
            @"C:\Apps\Browser\browser.exe",
            Arguments: "--demo",
            WorkingDirectory: null,
            EnvironmentVariables: new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Override",
                ["EXTRA"] = "VALUE"
            });

        profileSetup.InSequence(sequence)
            .Setup(s => s.EnsureProfileUnderToken(It.IsAny<AppContainerEntry>(), (IntPtr)10))
            .Returns(AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true));
        dataFolderService.InSequence(sequence)
            .Setup(s => s.EnsureContainerDataFolder(It.IsAny<AppContainerEntry>(), "S-1-15-2-42"));
        dataFolderService.InSequence(sequence)
            .Setup(s => s.EnsureDataFolderTraverse(It.IsAny<AppContainerEntry>(), "S-1-15-2-42"));
        dataFolderService.InSequence(sequence)
            .Setup(s => s.EnsureInteractiveUserAccess(It.IsAny<AppContainerEntry>()));
        tokenBuilder.InSequence(sequence)
            .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", null))
            .Returns(tokenContext.Context);
        profileSetup.InSequence(sequence)
            .Setup(s => s.TryEnableVirtualization(tokenContext.AppContainerToken));
        environmentSetup.InSequence(sequence)
            .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", target.ExePath))
            .Returns(CreateEnvironmentBlock(
                new Dictionary<string, string>
                {
                    ["PATH"] = @"C:\Base",
                    ["BASE"] = "1"
                }));

        Dictionary<string, string>? mergedEnvironment = null;
        ProcessLaunchTarget? launchedTarget = null;
        processStarter.InSequence(sequence)
            .Setup(s => s.Start(tokenContext.AppContainerToken, It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
            .Callback<IntPtr, ProcessLaunchTarget, IntPtr>((_, launchTarget, environmentPointer) =>
            {
                launchedTarget = launchTarget;
                mergedEnvironment = NativeEnvironmentBlockReader.Read(environmentPointer);
            })
            .Returns(new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 123, hProcess = (IntPtr)501, hThread = (IntPtr)502 });
        processStarter.InSequence(sequence)
            .Setup(s => s.GetImmediateExitCode(It.IsAny<ProcessLaunchNative.PROCESS_INFORMATION>()))
            .Returns((uint?)null);

        var launcher = CreateLauncher(
            profileSetup: profileSetup.Object,
            dataFolderService: dataFolderService.Object,
            tokenBuilder: tokenBuilder.Object,
            environmentSetup: environmentSetup.Object,
            processStarter: processStarter.Object);

        using var process = launcher.LaunchFile(target, CreateIdentity());

        Assert.NotNull(launchedTarget);
        Assert.Equal(@"C:\Apps\Browser", launchedTarget!.WorkingDirectory);
        Assert.NotNull(mergedEnvironment);
        Assert.Equal(@"C:\Override", mergedEnvironment!["PATH"]);
        Assert.Equal("VALUE", mergedEnvironment["EXTRA"]);
        Assert.Equal("1", mergedEnvironment["BASE"]);
    }

    [Theory]
    [InlineData(AppContainerLauncherFailurePoint.TokenBuild)]
    [InlineData(AppContainerLauncherFailurePoint.EnvironmentBuild)]
    [InlineData(AppContainerLauncherFailurePoint.ProcessStart)]
    public void LaunchFile_CollaboratorFailures_DisposeAcquiredResources(AppContainerLauncherFailurePoint failurePoint)
    {
        var tokenContext = CreateTokenContext();
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        var processStarter = new Mock<IAppContainerProcessStarter>();
        var environmentBlock = new TrackingEnvironmentBlock(
            new Dictionary<string, string> { ["PATH"] = @"C:\Base" });

        if (failurePoint == AppContainerLauncherFailurePoint.TokenBuild)
        {
            tokenBuilder
                .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", null))
                .Throws(new InvalidOperationException("token build failed"));
        }
        else
        {
            tokenBuilder
                .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", null))
                .Returns(tokenContext.Context);
        }

        if (failurePoint == AppContainerLauncherFailurePoint.EnvironmentBuild)
        {
            environmentSetup
                .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", It.IsAny<string>()))
                .Throws(new InvalidOperationException("environment failed"));
        }
        else if (failurePoint != AppContainerLauncherFailurePoint.TokenBuild)
        {
            environmentSetup
                .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", It.IsAny<string>()))
                .Returns(environmentBlock.Environment);
        }

        if (failurePoint == AppContainerLauncherFailurePoint.ProcessStart)
        {
            processStarter
                .Setup(s => s.Start(tokenContext.AppContainerToken, It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
                .Throws(new Win32Exception(8, "start failed"));
        }

        var launcher = CreateLauncher(
            tokenBuilder: tokenBuilder.Object,
            environmentSetup: environmentSetup.Object,
            processStarter: processStarter.Object);

        Assert.ThrowsAny<Exception>(() => launcher.LaunchFile(
            new ProcessLaunchTarget(@"C:\Apps\Browser\browser.exe"),
            CreateIdentity()));

        Assert.Equal(failurePoint is not AppContainerLauncherFailurePoint.TokenBuild, tokenContext.DisposeCalled);
        Assert.Equal(failurePoint == AppContainerLauncherFailurePoint.ProcessStart, environmentBlock.DisposeCalled);
    }

    [Theory]
    [InlineData(0xC0000135u, "DLL not found")]
    [InlineData(0xC0000142u, "DLL initialization failed")]
    [InlineData(0xC0000022u, "Access denied")]
    [InlineData(0xDEADBEEFu, "code 0xDEADBEEF")]
    public void LaunchFile_ImmediateExit_UsesMappedFailureMessage(uint exitCode, string expectedMessagePart)
    {
        var tokenContext = CreateTokenContext();
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        tokenBuilder
            .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", null))
            .Returns(tokenContext.Context);

        var environmentBlock = new TrackingEnvironmentBlock(
            new Dictionary<string, string> { ["PATH"] = @"C:\Base" });
        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        environmentSetup
            .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", It.IsAny<string>()))
            .Returns(environmentBlock.Environment);

        var processStarter = new Mock<IAppContainerProcessStarter>();
        processStarter
            .Setup(s => s.Start(tokenContext.AppContainerToken, It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
            .Returns(new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 123 });
        processStarter
            .Setup(s => s.GetImmediateExitCode(It.IsAny<ProcessLaunchNative.PROCESS_INFORMATION>()))
            .Throws(new InvalidOperationException($"Process exited immediately (code 0x{exitCode:X8}). {expectedMessagePart}"));

        var launcher = CreateLauncher(
            tokenBuilder: tokenBuilder.Object,
            environmentSetup: environmentSetup.Object,
            processStarter: processStarter.Object);

        var exception = Assert.Throws<InvalidOperationException>(() => launcher.LaunchFile(
            new ProcessLaunchTarget(@"C:\Apps\Browser\browser.exe"),
            CreateIdentity()));

        Assert.Contains(expectedMessagePart, exception.Message, StringComparison.Ordinal);
        Assert.True(tokenContext.DisposeCalled);
        Assert.True(environmentBlock.DisposeCalled);
    }

    [Fact]
    public void LaunchFile_WithCapabilities_PassesCapabilitiesToTokenBuilder_AndDisposesTokenContext()
    {
        var capabilities = (IReadOnlyList<string>)["S-1-15-3-1", "S-1-15-3-2"];
        var tokenContext = CreateTokenContext(
            capabilitySidPointers: [(IntPtr)102, (IntPtr)103],
            capabilityArrayPointer: (IntPtr)104);
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        tokenBuilder
            .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", capabilities))
            .Returns(tokenContext.Context);

        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        environmentSetup
            .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", It.IsAny<string>()))
            .Returns(CreateEnvironmentBlock(new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base"
            }));

        var processStarter = new Mock<IAppContainerProcessStarter>();
        processStarter
            .Setup(s => s.Start(tokenContext.AppContainerToken, It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
            .Returns(new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 123, hProcess = (IntPtr)501, hThread = (IntPtr)502 });
        processStarter
            .Setup(s => s.GetImmediateExitCode(It.IsAny<ProcessLaunchNative.PROCESS_INFORMATION>()))
            .Returns((uint?)null);

        var launcher = CreateLauncher(
            tokenBuilder: tokenBuilder.Object,
            environmentSetup: environmentSetup.Object,
            processStarter: processStarter.Object);

        using var process = launcher.LaunchFile(
            new ProcessLaunchTarget(@"C:\Apps\Browser\browser.exe"),
            CreateIdentity(capabilities));

        Assert.True(tokenContext.DisposeCalled);
        Assert.Equal([(IntPtr)102, (IntPtr)103, (IntPtr)101], tokenContext.LocalFreedPointers);
        Assert.Equal([(IntPtr)104], tokenContext.CapabilityArraysFreed);
        Assert.Equal([(IntPtr)30, (IntPtr)20], tokenContext.ClosedHandles);
    }

    [Fact]
    public void LaunchFile_WithCapabilities_AndProcessStartFailure_ReleasesCapabilityResources()
    {
        var capabilities = (IReadOnlyList<string>)["S-1-15-3-1", "S-1-15-3-2"];
        var tokenContext = CreateTokenContext(
            capabilitySidPointers: [(IntPtr)102, (IntPtr)103],
            capabilityArrayPointer: (IntPtr)104);
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        tokenBuilder
            .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", capabilities))
            .Returns(tokenContext.Context);

        var environmentBlock = new TrackingEnvironmentBlock(
            new Dictionary<string, string> { ["PATH"] = @"C:\Base" });
        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        environmentSetup
            .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", It.IsAny<string>()))
            .Returns(environmentBlock.Environment);

        var processStarter = new Mock<IAppContainerProcessStarter>();
        processStarter
            .Setup(s => s.Start(tokenContext.AppContainerToken, It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
            .Throws(new Win32Exception(8, "process creation failed"));

        var launcher = CreateLauncher(
            tokenBuilder: tokenBuilder.Object,
            environmentSetup: environmentSetup.Object,
            processStarter: processStarter.Object);

        Assert.Throws<Win32Exception>(() => launcher.LaunchFile(
            new ProcessLaunchTarget(@"C:\Apps\Browser\browser.exe"),
            CreateIdentity(capabilities)));

        Assert.True(tokenContext.DisposeCalled);
        Assert.True(environmentBlock.DisposeCalled);
        Assert.Equal([(IntPtr)102, (IntPtr)103, (IntPtr)101], tokenContext.LocalFreedPointers);
        Assert.Equal([(IntPtr)104], tokenContext.CapabilityArraysFreed);
        Assert.Equal([(IntPtr)30, (IntPtr)20], tokenContext.ClosedHandles);
    }

    private static AppContainerProcessLauncher CreateLauncher(
        IAppContainerProfileSetup? profileSetup = null,
        IAppContainerDataFolderService? dataFolderService = null,
        IAppContainerSidProvider? sidProvider = null,
        IAppContainerTokenBuilder? tokenBuilder = null,
        IAppContainerEnvironmentSetup? environmentSetup = null,
        IAppContainerProcessStarter? processStarter = null)
    {
        var effectiveProfileSetup = profileSetup ?? CreateProfileSetup();
        var effectiveDataFolderService = dataFolderService ?? new Mock<IAppContainerDataFolderService>().Object;
        var effectiveSidProvider = sidProvider ?? CreateSidProvider();
        var effectiveTokenBuilder = tokenBuilder ?? CreateTokenBuilder();
        var effectiveEnvironmentSetup = environmentSetup ?? CreateEnvironmentSetup();
        var effectiveProcessStarter = processStarter ?? CreateProcessStarter();

        return new AppContainerProcessLauncher(
            new Mock<ILoggingService>().Object,
            effectiveEnvironmentSetup,
            effectiveProfileSetup,
            effectiveDataFolderService,
            CreateExplorerTokenProvider(),
            effectiveSidProvider,
            effectiveTokenBuilder,
            effectiveProcessStarter);
    }

    private static IAppContainerProfileSetup CreateProfileSetup()
    {
        var profileSetup = new Mock<IAppContainerProfileSetup>();
        profileSetup
            .Setup(s => s.EnsureProfileUnderToken(It.IsAny<AppContainerEntry>(), It.IsAny<IntPtr>()))
            .Returns(AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true));
        return profileSetup.Object;
    }

    private static IAppContainerSidProvider CreateSidProvider()
    {
        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_browser")).Returns("S-1-15-2-42");
        return sidProvider.Object;
    }

    private static IAppContainerTokenBuilder CreateTokenBuilder()
    {
        var tokenBuilder = new Mock<IAppContainerTokenBuilder>();
        tokenBuilder
            .Setup(b => b.Build((IntPtr)10, "S-1-15-2-42", It.IsAny<IReadOnlyList<string>?>()))
            .Returns(() => CreateTokenContext().Context);
        return tokenBuilder.Object;
    }

    private static IAppContainerEnvironmentSetup CreateEnvironmentSetup()
    {
        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        environmentSetup
            .Setup(s => s.CreateLaunchEnvironment((IntPtr)10, It.IsAny<AppContainerEntry>(), "S-1-15-2-42", It.IsAny<string>()))
            .Returns(CreateEnvironmentBlock(new Dictionary<string, string>
            {
                ["PATH"] = @"C:\Base"
            }));
        return environmentSetup.Object;
    }

    private static IAppContainerProcessStarter CreateProcessStarter()
    {
        var processStarter = new Mock<IAppContainerProcessStarter>();
        processStarter
            .Setup(s => s.Start(It.IsAny<IntPtr>(), It.IsAny<ProcessLaunchTarget>(), It.IsAny<IntPtr>()))
            .Returns(new ProcessLaunchNative.PROCESS_INFORMATION { dwProcessId = 123 });
        processStarter
            .Setup(s => s.GetImmediateExitCode(It.IsAny<ProcessLaunchNative.PROCESS_INFORMATION>()))
            .Returns((uint?)null);
        return processStarter.Object;
    }

    private static IExplorerTokenProvider CreateExplorerTokenProvider()
    {
        var provider = new Mock<IExplorerTokenProvider>();
        provider.Setup(p => p.GetExplorerToken()).Returns((IntPtr)10);
        return provider.Object;
    }

    private static AppContainerLaunchIdentity CreateIdentity(IReadOnlyList<string>? capabilities = null)
        => new(new AppContainerEntry
        {
            Name = "ram_browser",
            DisplayName = "Browser",
            Sid = "S-1-15-2-42",
            Capabilities = capabilities?.ToList()
        });

    private static EnvironmentBlock CreateEnvironmentBlock(IEnumerable<KeyValuePair<string, string>> variables)
    {
        using var block = EnvironmentBlock.Build(variables.ToDictionary(static pair => pair.Key, static pair => pair.Value));
        return EnvironmentBlock.Own(block.Detach(), Marshal.FreeHGlobal);
    }

    private static TrackingTokenContext CreateTokenContext(
        IReadOnlyList<IntPtr>? capabilitySidPointers = null,
        IntPtr capabilityArrayPointer = default)
    {
        AppContainerLaunchTokenContext? context = null;
        var tracking = new TrackingTokenContext(() => context!);
        context = new AppContainerLaunchTokenContext(
            duplicatedExplorerToken: (IntPtr)20,
            appContainerToken: (IntPtr)30,
            interactiveUserSid: "S-1-5-21-100-100-100-1000",
            capabilitySidPointers: capabilitySidPointers ?? Array.Empty<IntPtr>(),
            capabilityArrayPointer: capabilityArrayPointer,
            containerSidPointer: (IntPtr)101,
            localFree: pointer =>
            {
                tracking.DisposeCalled = true;
                tracking.LocalFreedPointers.Add(pointer);
            },
            capabilityArrayFree: pointer =>
            {
                tracking.DisposeCalled = true;
                tracking.CapabilityArraysFreed.Add(pointer);
            },
            closeHandle: handle =>
            {
                tracking.DisposeCalled = true;
                tracking.ClosedHandles.Add(handle);
            });
        return tracking;
    }

    private sealed class TrackingTokenContext
    {
        private readonly Func<AppContainerLaunchTokenContext> _contextAccessor;

        public TrackingTokenContext(Func<AppContainerLaunchTokenContext> contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        public AppContainerLaunchTokenContext Context => _contextAccessor();

        public bool DisposeCalled { get; set; }

        public List<IntPtr> LocalFreedPointers { get; } = [];

        public List<IntPtr> CapabilityArraysFreed { get; } = [];

        public List<IntPtr> ClosedHandles { get; } = [];

        public IntPtr AppContainerToken => Context.AppContainerToken;
    }

    private sealed class TrackingEnvironmentBlock
    {
        public TrackingEnvironmentBlock(IReadOnlyDictionary<string, string> variables)
        {
            using var block = EnvironmentBlock.Build(variables);
            Environment = EnvironmentBlock.Own(block.Detach(), _ => DisposeCalled = true);
        }

        public EnvironmentBlock Environment { get; }

        public bool DisposeCalled { get; private set; }
    }

    public enum AppContainerLauncherFailurePoint
    {
        TokenBuild,
        EnvironmentBuild,
        ProcessStart
    }
}

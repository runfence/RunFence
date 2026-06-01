using Microsoft.Win32;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class LaunchTargetResolverTests : IDisposable
{
    private const string TestSid = "S-1-5-21-7777-8888-9999-1001";
    private const string InteractiveSid = "S-1-5-21-7777-8888-9999-2001";
    private const string LauncherPath = @"C:\RunFence\RunFence.Launcher.exe";
    private const string RunFenceExePath = @"C:\RunFence\RunFence.exe";

    private readonly RegistryTestHelper _registry = new("LaunchTargetResolverHku", "LaunchTargetResolverHklm");
    private readonly Mock<IInteractiveUserResolver> _interactiveUserResolver = new();
    private readonly Mock<IAssociationLaunchResolver> _associationLaunchResolver = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly IUiThreadInvoker _uiThreadInvoker = new InlineUiThreadInvoker(action => action());
    private readonly Mock<ILoggingService> _log = new();
    private readonly AppDatabase _database = new();

    public LaunchTargetResolverTests()
    {
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(InteractiveSid);
        _associationLaunchResolver
            .Setup(r => r.Resolve(It.IsAny<AppDatabase>(), It.IsAny<AssociationLaunchRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(new AssociationLaunchResolution(AssociationLaunchResolutionStatus.UnknownAssociation));
        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32)));
    }

    public void Dispose() => _registry.Dispose();

    private LaunchTargetResolver CreateResolver(
        IShortcutGateway? shortcutGateway = null,
        IAssociationExecutablePathResolver? associationExecutablePathResolver = null,
        IUiThreadInvoker? uiThreadInvoker = null,
        ISessionProvider? sessionProvider = null)
    {
        var registryResolver = new AssociationRegistryResolver(
            _log.Object,
            new TestHklmClassesRootProvider(_registry.HklmRoot),
            new TestHkuRootProvider(_registry.HkuRoot),
            new AssociationRegistryProtocolMarkerReader());
        var executablePathResolver = associationExecutablePathResolver ?? CreateValidAssociationExecutablePathResolver();
        var materializer = new AssociationCommandMaterializer(_log.Object, executablePathResolver);
        var leaseCoordinator = new LaunchHiveLeaseCoordinator(_registry.HiveManager.Object);
        var candidateResolver = new AssociationLaunchCandidateResolver(
            registryResolver,
            materializer,
            _associationLaunchResolver.Object,
            _log.Object);

        var shortcutTargetResolver = new ShortcutTargetResolver(shortcutGateway!);
        return new LaunchTargetResolver(
            _interactiveUserResolver.Object,
            candidateResolver,
            new UiThreadDatabaseAccessor(
                new LambdaDatabaseProvider(() => (sessionProvider ?? _sessionProvider.Object).GetSession().Database),
                () => uiThreadInvoker ?? _uiThreadInvoker),
            leaseCoordinator,
            shortcutTargetResolver,
            _log.Object);
    }

    private static IAssociationExecutablePathResolver CreateValidAssociationExecutablePathResolver()
    {
        var resolver = new Mock<IAssociationExecutablePathResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string>()))
            .Returns<string>(exePath => AssociationExecutablePathResolution.Valid(exePath));
        return resolver.Object;
    }

    private static AccountLaunchIdentity BasicIdentity(string sid = TestSid)
        => new(sid) { PrivilegeLevel = PrivilegeLevel.Isolated };

    private static AccountLaunchIdentity BasicIdentityAllowingAccountRedirection(string sid = TestSid)
        => new(sid)
        {
            PrivilegeLevel = PrivilegeLevel.Isolated,
            AssociationResolutionPolicy = AssociationResolutionPolicy.AllowAccountRedirection
        };

    private static AccountLaunchIdentity HighestAllowedIdentity(string sid = TestSid)
        => new(sid) { PrivilegeLevel = PrivilegeLevel.HighestAllowed };

    private static AccountLaunchIdentity HighestAllowedIdentityAllowingAccountRedirection(string sid = TestSid)
        => new(sid)
        {
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
            AssociationResolutionPolicy = AssociationResolutionPolicy.AllowAccountRedirection
        };

    private static ProcessLaunchTarget FileTarget(string path)
        => new(path);

    private static string? ExeName(ProcessLaunchTarget target)
        => Path.GetFileName(target.ExePath);

    private void SetFileUserChoice(string sid, string ext, string progId)
    {
        using var key = _registry.HkuRoot.CreateSubKey(
            $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
        key.SetValue("ProgId", progId);
    }

    private void SetUrlUserChoice(string sid, string scheme, string progId)
    {
        using var key = _registry.HkuRoot.CreateSubKey(
            $@"{sid}\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice");
        key.SetValue("ProgId", progId);
    }

    private void SetUserExtension(string sid, string ext, string progId)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{sid}\Software\Classes\{ext}");
        key.SetValue(null, progId);
    }

    private void SetMachineExtension(string ext, string progId)
    {
        using var key = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{ext}");
        key.SetValue(null, progId);
    }

    private void SetUserProgIdCommand(string sid, string progId, string command)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{sid}\Software\Classes\{progId}\shell\open\command");
        key.SetValue(null, command);
    }

    private void SetMachineProgIdCommand(string progId, string command)
    {
        using var key = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{progId}\shell\open\command");
        key.SetValue(null, command);
    }

    private void SetUserProtocol(string sid, string scheme, string command, bool addUrlProtocol = true)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{sid}\Software\Classes\{scheme}");
        if (addUrlProtocol)
            key.SetValue("URL Protocol", string.Empty);
        using var cmdKey = _registry.HkuRoot.CreateSubKey($@"{sid}\Software\Classes\{scheme}\shell\open\command");
        cmdKey.SetValue(null, command);
    }

    private void SetMachineProtocol(string scheme, string command)
    {
        using var key = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{scheme}\shell\open\command");
        key.SetValue(null, command);
    }

    private void SetMachineProtocolDelegateExecute(string scheme, string clsid)
    {
        using var protocolKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{scheme}");
        protocolKey.SetValue("URL Protocol", string.Empty);
        using var commandKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{scheme}\shell\open\command");
        commandKey.SetValue("DelegateExecute", clsid);
    }

    private void SetMachineProtocolCommandAndDelegateExecute(string scheme, string command, string clsid)
    {
        using var protocolKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{scheme}");
        protocolKey.SetValue("URL Protocol", string.Empty);
        using var commandKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{scheme}\shell\open\command");
        commandKey.SetValue(null, command);
        commandKey.SetValue("DelegateExecute", clsid);
    }

    private void SetMachineProgIdDelegateExecute(string progId, string clsid)
    {
        using var commandKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{progId}\shell\open\command");
        commandKey.SetValue("DelegateExecute", clsid);
    }

    [Fact]
    public void ResolveAssociation_DirectExecutable_PassesThroughUnchanged()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\Apps\tool.exe", "--flag", @"C:\Work");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget);

        Assert.Same(originalTarget, result.Target);
        Assert.NotEqual(LaunchResolutionKind.ShellWrapped, result.Kind);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ResolveAssociation_Script_UsesCurrentWrapperBehaviorWithoutRegistryResolution()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\Scripts\run.bat", "--flag", @"C:\Work", SuppressStartupFeedback: true);

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget);

        Assert.Equal("cmd.exe", result.Target.ExePath);
        Assert.Contains(@"""C:\Scripts\run.bat""", result.Target.Arguments);
        Assert.True(result.Target.SuppressStartupFeedback);
        Assert.NotEqual(LaunchResolutionKind.ShellWrapped, result.Kind);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ResolveAssociation_UsesPassedExtensionForDirectDetection()
    {
        var originalTarget = new ProcessLaunchTarget(
            @"C:\Targets\tool.pdf");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget, ".exe");

        Assert.Same(originalTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.Direct, result.Kind);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ResolveAssociation_UsesPassedExtensionForScriptWrapping()
    {
        var originalTarget = new ProcessLaunchTarget(
            @"C:\Targets\run.exe",
            "--flag",
            @"C:\Work",
            SuppressStartupFeedback: true);

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget, ".bat");

        Assert.Equal("cmd.exe", result.Target.ExePath);
        Assert.Contains(@"""C:\Targets\run.exe""", result.Target.Arguments);
        Assert.True(result.Target.SuppressStartupFeedback);
        Assert.Equal(LaunchResolutionKind.Script, result.Kind);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ResolveAssociation_ResolvedHandler_PreservesSuppressStartupFeedback()
    {
        SetMachineExtension(".pdf", "Pdf.Handler");
        SetMachineProgIdCommand("Pdf.Handler", @"""C:\Apps\viewer.exe"" ""%1""");
        var originalTarget = new ProcessLaunchTarget(@"C:\Docs\report.pdf", SuppressStartupFeedback: true);

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget);

        Assert.Equal("viewer.exe", ExeName(result.Target));
        Assert.True(result.Target.SuppressStartupFeedback);
    }

    [Fact]
    public void ResolveAssociation_UsesPassedExtensionForAssociationLookup()
    {
        SetMachineExtension(".pdf", "Pdf.Handler");
        SetMachineProgIdCommand("Pdf.Handler", @"""C:\Apps\viewer.exe"" ""%1""");
        var originalTarget = new ProcessLaunchTarget(
            @"C:\Targets\report.exe");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget, ".pdf");

        Assert.Equal("viewer.exe", ExeName(result.Target));
        Assert.Equal(@"""C:\Targets\report.exe""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_UserChoiceProgId_WinsOverUserExtensionMapping()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", @"""C:\Apps\choice.exe"" ""%1""");
        SetUserExtension(TestSid, ".pdf", "Other.Pdf");
        SetUserProgIdCommand(TestSid, "Other.Pdf", @"""C:\Apps\other.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("choice.exe", ExeName(result.Target));
        Assert.Equal(@"""C:\Docs\report.pdf""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_MissingUserChoiceExecutable_SkipsToFallbackCandidate()
    {
        SetFileUserChoice(TestSid, ".log", "Choice.Log");
        SetUserProgIdCommand(TestSid, "Choice.Log", @"""C:\Missing\stale.exe"" ""%1""");
        SetUserExtension(TestSid, ".log", "Fallback.Log");
        SetUserProgIdCommand(TestSid, "Fallback.Log", @"""C:\Apps\fallback.exe"" ""%1""");
        var executablePathResolver = new Mock<IAssociationExecutablePathResolver>();
        executablePathResolver.Setup(r => r.Resolve(@"C:\Missing\stale.exe"))
            .Returns(AssociationExecutablePathResolution.Invalid(@"C:\Missing\stale.exe", "rooted executable path does not exist"));
        executablePathResolver.Setup(r => r.Resolve(@"C:\Apps\fallback.exe"))
            .Returns(AssociationExecutablePathResolution.Valid(@"C:\Apps\fallback.exe"));

        using var result = CreateResolver(associationExecutablePathResolver: executablePathResolver.Object)
            .ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\runfence.log"));

        Assert.Equal(@"C:\Apps\fallback.exe", result.Target.ExePath);
        Assert.Equal(@"""C:\Docs\runfence.log""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_RepairedExecutablePath_PreservesAssociationArguments()
    {
        var stalePath = Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2510.14.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");
        var repairedPath = Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2512.1.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");
        SetFileUserChoice(TestSid, ".log", "Choice.Log");
        SetUserProgIdCommand(TestSid, "Choice.Log", $@"""{stalePath}"" ""%1""");
        var executablePathResolver = new Mock<IAssociationExecutablePathResolver>();
        executablePathResolver.Setup(r => r.Resolve(stalePath))
            .Returns(AssociationExecutablePathResolution.Valid(repairedPath, wasRepaired: true));

        using var result = CreateResolver(associationExecutablePathResolver: executablePathResolver.Object)
            .ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\runfence.log"));

        Assert.Equal(repairedPath, result.Target.ExePath);
        Assert.Equal(@"""C:\Docs\runfence.log""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_LaunchedAccountSuccess_KeepsLoadedHiveLeaseUntilReturnedLeaseIsDisposed()
    {
        var launchedLease = new TrackingDisposable();
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(TestSid)).Returns(launchedLease);

        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", @"""C:\Apps\choice.exe"" ""%1""");

        var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("choice.exe", ExeName(result.Target));
        Assert.NotEqual(LaunchResolutionKind.ShellWrapped, result.Kind);
        Assert.False(launchedLease.IsDisposed);

        result.Dispose();

        Assert.True(launchedLease.WaitUntilDisposed());
    }

    [Fact]
    public void ResolveAssociation_InvalidUserChoice_ContinuesToRemainingFileSources()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", $"\"{LauncherPath}\" --resolve \"%1\"");
        SetUserExtension(TestSid, ".pdf", "Other.Pdf");
        SetUserProgIdCommand(TestSid, "Other.Pdf", @"""C:\Apps\other.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("other.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_MalformedCommand_ContinuesToRemainingFileSources()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", @"""C:\Apps\broken.exe %1");
        SetUserExtension(TestSid, ".pdf", "Other.Pdf");
        SetUserProgIdCommand(TestSid, "Other.Pdf", @"""C:\Apps\other.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("other.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_ValidCommandWithBackslashesBeforeClosingQuote_IsNotRejectedAsMalformed()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", @"""C:\Apps\viewer.exe"" ""--dir=C:\Temp\\"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("viewer.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_CommandWithoutSupportedPlaceholder_AppendsSpacedFilePathAsSingleArgument()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", @"""C:\Apps\viewer.exe"" --open");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\My File.pdf"));

        Assert.Equal("viewer.exe", ExeName(result.Target));
        Assert.Equal(@"--open ""C:\Docs\My File.pdf""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_CommandWithTabSeparators_ParsesAndMaterializesCorrectly()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", "\"C:\\Apps\\viewer.exe\"\t\"%1\"");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\My File.pdf"));

        Assert.Equal("viewer.exe", ExeName(result.Target));
        Assert.Equal(@"""C:\Docs\My File.pdf""", result.Target.Arguments);
    }

    [Theory]
    [InlineData("%1")]
    [InlineData("%L")]
    [InlineData("%l")]
    [InlineData("%V")]
    [InlineData("%v")]
    [InlineData("%U")]
    [InlineData("%u")]
    [InlineData("%*")]
    public void ResolveAssociation_CommandWithSupportedPlaceholder_ReplacesAllOccurrencesWithoutAppendingDuplicate(string placeholder)
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", $@"""C:\Apps\viewer.exe"" --first=""{placeholder}"" --second=""{placeholder}""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("viewer.exe", ExeName(result.Target));
        Assert.Equal(@"--first=""C:\Docs\report.pdf"" --second=""C:\Docs\report.pdf""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_UnsupportedAssociationPlaceholder_IsRejectedAndFallsBackToRemainingSources()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", @"""C:\Apps\viewer.exe"" ""%2""");
        SetUserExtension(TestSid, ".pdf", "Other.Pdf");
        SetUserProgIdCommand(TestSid, "Other.Pdf", @"""C:\Apps\other.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("other.exe", ExeName(result.Target));
        _log.Verify(
            l => l.Debug(It.Is<string>(message =>
                message.Contains("rejected", StringComparison.Ordinal)
                && message.Contains("unsupported association placeholder '%2'", StringComparison.Ordinal))),
            Times.AtLeastOnce);
    }

    [Fact]
    public void ResolveAssociation_RunFenceUserChoiceProgId_ContinuesToRemainingFileSources()
    {
        SetFileUserChoice(TestSid, ".pdf", PathConstants.HandlerProgIdPrefix + ".pdf");
        SetUserExtension(TestSid, ".pdf", "Other.Pdf");
        SetUserProgIdCommand(TestSid, "Other.Pdf", @"""C:\Apps\other.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("other.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_RunFenceAssociationLauncherCommand_IsAccepted()
    {
        SetFileUserChoice(TestSid, ".pdf", PathConstants.HandlerProgIdPrefix + ".pdf");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + ".pdf", $"\"{LauncherPath}\" --resolve \".pdf\" \"%L\"");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == ".pdf" && x.RawArgument == @"""C:\Docs\report.pdf"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry
                {
                    Id = "browser01",
                    AccountSid = TestSid,
                    ExePath = @"C:\Apps\browser.exe",
                    AllowPassingArguments = true
                },
                new HandlerMappingEntry("browser01")));

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
        Assert.Equal(@"--resolve "".pdf"" ""C:\Docs\report.pdf""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_RunFenceAssociationLauncherCommand_ForDifferentAccount_AllowingAccountRedirection_IsAccepted()
    {
        SetFileUserChoice(TestSid, ".pdf", PathConstants.HandlerProgIdPrefix + ".pdf");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + ".pdf", $"\"{LauncherPath}\" --resolve \".pdf\" \"%1\"");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == ".pdf" && x.RawArgument == @"""C:\Docs\report.pdf"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry
                {
                    Id = "browser01b",
                    AccountSid = "S-1-5-21-other",
                    ExePath = @"C:\Apps\browser.exe",
                    AllowPassingArguments = true
                },
                new HandlerMappingEntry("browser01b")));

        using var result = CreateResolver().ResolveFileHandler(
            BasicIdentityAllowingAccountRedirection(),
            FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
        Assert.Equal(@"--resolve "".pdf"" ""C:\Docs\report.pdf""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_RunFenceExecutableCommand_ContinuesToRemainingFileSources()
    {
        SetFileUserChoice(TestSid, ".pdf", "Choice.Pdf");
        SetUserProgIdCommand(TestSid, "Choice.Pdf", $"\"{RunFenceExePath}\" --open \"%1\"");
        SetUserExtension(TestSid, ".pdf", "Other.Pdf");
        SetUserProgIdCommand(TestSid, "Other.Pdf", @"""C:\Apps\other.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("other.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_UserExtensionProgId_FallsBackToMachineProgIdCommand()
    {
        SetUserExtension(TestSid, ".txt", "Shared.Txt");
        SetMachineProgIdCommand("Shared.Txt", @"""C:\Windows\system32\notepad.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\notes.txt"));

        Assert.Equal("notepad.exe", ExeName(result.Target));
        Assert.Equal(@"""C:\Docs\notes.txt""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveAssociation_NoUserExtension_FallsBackToMachineExtensionProgId()
    {
        SetMachineExtension(".txt", "txtfile");
        SetMachineProgIdCommand("txtfile", @"""C:\Windows\system32\notepad.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\notes.txt"));

        Assert.Equal("notepad.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_InteractiveFallback_ReusesResolutionPipeline()
    {
        SetFileUserChoice(InteractiveSid, ".pdf", "Interactive.Pdf");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Pdf", @"""C:\Apps\interactive.exe"" ""%1""");

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("interactive.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveAssociation_InteractiveFallback_KeepsBothHiveLeasesAliveUntilReturnedLeaseIsDisposed()
    {
        var launchedLease = new TrackingDisposable();
        var interactiveLease = new TrackingDisposable();
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(TestSid)).Returns(launchedLease);
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(InteractiveSid)).Returns(interactiveLease);

        SetFileUserChoice(InteractiveSid, ".pdf", "Interactive.Pdf");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Pdf", @"""C:\Apps\interactive.exe"" ""%1""");

        var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal("interactive.exe", ExeName(result.Target));
        Assert.NotEqual(LaunchResolutionKind.ShellWrapped, result.Kind);
        Assert.False(launchedLease.IsDisposed);
        Assert.False(interactiveLease.IsDisposed);

        result.Dispose();

        Assert.True(launchedLease.WaitUntilDisposed());
        Assert.True(interactiveLease.WaitUntilDisposed());
    }

    [Fact]
    public void ResolveAssociation_InteractiveFallback_RejectsHandlersUnderUsers()
    {
        SetFileUserChoice(InteractiveSid, ".pdf", "Interactive.Pdf");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Pdf", @"""C:\Users\Test\AppData\Local\Viewer\viewer.exe"" ""%1""");

        var originalTarget = FileTarget(@"C:\Docs\report.pdf");
        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), originalTarget);
        var legacyTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveAssociation_InteractiveFallback_RunFenceAssociationLauncher_SameSid_IsAccepted()
    {
        SetFileUserChoice(InteractiveSid, ".pdf", PathConstants.HandlerProgIdPrefix + ".pdf");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + ".pdf", $"\"{LauncherPath}\" --resolve \".pdf\" \"%1\"");
        SetUserExtension(InteractiveSid, ".pdf", "Interactive.Pdf");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Pdf", @"""C:\Apps\direct-viewer.exe"" ""%1""");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == ".pdf" && x.RawArgument == @"""C:\Docs\report.pdf"""),
                null,
                InteractiveSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry { Id = "viewer04", AccountSid = InteractiveSid, ExePath = @"C:\Users\Test\AppData\Local\Viewer\viewer.exe" },
                new HandlerMappingEntry("viewer04")));

        using var result = CreateResolver().ResolveFileHandler(BasicIdentity(), FileTarget(@"C:\Docs\report.pdf"));

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
    }

    [Fact]
    public void ResolveAssociation_NoUsableHandler_HighestAllowedThrows()
    {
        var resolver = CreateResolver();

        var ex = Assert.Throws<AssociationResolutionException>(() =>
            resolver.ResolveFileHandler(HighestAllowedIdentity(), FileTarget(@"C:\Docs\report.pdf")));
        Assert.Equal("No usable association handler found for 'C:\\Docs\\report.pdf'.", ex.Message);
    }

    [Fact]
    public void ResolveAssociation_NoUsableHandler_HighestAllowedAllowingAccountRedirection_FallsBackToLegacyWrappedTarget()
    {
        var originalTarget = FileTarget(@"C:\Docs\report.pdf");
        var resolver = CreateResolver();

        using var result = resolver.ResolveFileHandler(HighestAllowedIdentityAllowingAccountRedirection(), originalTarget);
        var legacyTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveAssociation_NoUsableHandler_BasicFallsBackToLegacyWrappedTarget()
    {
        var originalTarget = FileTarget(@"C:\Docs\report.pdf");
        var resolver = CreateResolver();

        using var result = resolver.ResolveFileHandler(BasicIdentity(), originalTarget);
        var legacyTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveAssociation_NoUsableHandler_BasicFallback_KeepsLoadedHiveLeasesUntilReturnedLeaseIsDisposed()
    {
        var launchedLease = new TrackingDisposable();
        var interactiveLease = new TrackingDisposable();
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(TestSid)).Returns(launchedLease);
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(InteractiveSid)).Returns(interactiveLease);

        var originalTarget = FileTarget(@"C:\Docs\report.pdf");
        var resolver = CreateResolver();

        var result = resolver.ResolveFileHandler(BasicIdentity(), originalTarget);
        var legacyTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
        Assert.False(launchedLease.IsDisposed);
        Assert.False(interactiveLease.IsDisposed);

        result.Dispose();

        Assert.True(launchedLease.WaitUntilDisposed());
        Assert.True(interactiveLease.WaitUntilDisposed());
    }

    [Fact]
    public void ResolveUrlTarget_UserChoiceProgId_WinsOverDirectProtocolHandler()
    {
        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", @"""C:\Apps\choice-mail.exe"" ""%1""");
        SetUserProtocol(TestSid, "mailto", @"""C:\Apps\direct-mail.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("choice-mail.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_LaunchedAccountSuccess_KeepsLoadedHiveLeaseUntilReturnedLeaseIsDisposed()
    {
        var launchedLease = new TrackingDisposable();
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(TestSid)).Returns(launchedLease);

        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", @"""C:\Apps\choice-mail.exe"" ""%1""");

        var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("choice-mail.exe", ExeName(result.Target));
        Assert.NotEqual(LaunchResolutionKind.ShellWrapped, result.Kind);
        Assert.False(launchedLease.IsDisposed);

        result.Dispose();

        Assert.True(launchedLease.WaitUntilDisposed());
    }

    [Fact]
    public void ResolveUrlTarget_InvalidUserChoice_ContinuesToDirectProtocolHandler()
    {
        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", $"\"{LauncherPath}\" --resolve \"%1\"");
        SetUserProtocol(TestSid, "mailto", @"""C:\Apps\direct-mail.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("direct-mail.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_MalformedCommand_ContinuesToDirectProtocolHandler()
    {
        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", @"""C:\Apps\broken-mail.exe %1");
        SetUserProtocol(TestSid, "mailto", @"""C:\Apps\direct-mail.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("direct-mail.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_CommandWithoutSupportedPlaceholder_AppendsUrlAsSingleArgument()
    {
        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", @"""C:\Apps\mail.exe"" --open");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("mail.exe", ExeName(result.Target));
        Assert.Equal(@"--open mailto:test@example.com", result.Target.Arguments);
    }

    [Theory]
    [InlineData("%1")]
    [InlineData("%L")]
    [InlineData("%l")]
    [InlineData("%V")]
    [InlineData("%v")]
    [InlineData("%U")]
    [InlineData("%u")]
    [InlineData("%*")]
    public void ResolveUrlTarget_CommandWithSupportedPlaceholder_ReplacesAllOccurrencesWithoutAppendingDuplicate(string placeholder)
    {
        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", $@"""C:\Apps\mail.exe"" --first=""{placeholder}"" --second=""{placeholder}""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("mail.exe", ExeName(result.Target));
        Assert.Equal(@"--first=""mailto:test@example.com"" --second=""mailto:test@example.com""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveUrlTarget_CommandWithTabSeparators_ParsesAndMaterializesCorrectly()
    {
        SetUrlUserChoice(TestSid, "https", "Choice.Web");
        SetUserProgIdCommand(TestSid, "Choice.Web", "\"C:\\Apps\\browser.exe\"\t\"%U\"");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://example.com/a%20b");

        Assert.Equal("browser.exe", ExeName(result.Target));
        Assert.Equal(@"""https://example.com/a%20b""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveUrlTarget_RunFenceExecutableCommand_ContinuesToDirectProtocolHandler()
    {
        SetUrlUserChoice(TestSid, "mailto", "Choice.Mail");
        SetUserProgIdCommand(TestSid, "Choice.Mail", $"\"{RunFenceExePath}\" --open \"%1\"");
        SetUserProtocol(TestSid, "mailto", @"""C:\Apps\direct-mail.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "mailto:test@example.com");

        Assert.Equal("direct-mail.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_RunFenceAssociationLauncherCommand_IsAccepted()
    {
        SetUrlUserChoice(TestSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%U\"");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry
                {
                    Id = "browser01",
                    AccountSid = TestSid,
                    ExePath = @"C:\Apps\browser.exe",
                    AllowPassingArguments = true
                },
                new HandlerMappingEntry("browser01")));

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/");

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
        Assert.Equal(@"--resolve ""https"" ""https://github.com/RunFence/""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveUrlTarget_RunFenceAssociationLauncherCommand_ForDifferentAccount_ContinuesToDirectProtocolHandler()
    {
        SetUrlUserChoice(TestSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");
        SetUserProtocol(TestSid, "https", @"""C:\Apps\direct-browser.exe"" ""%1""");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry
                {
                    Id = "browser02",
                    AccountSid = "S-1-5-21-other",
                    ExePath = @"C:\Apps\other.exe",
                    AllowPassingArguments = true
                },
                new HandlerMappingEntry("browser02")));

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/");

        Assert.Equal("direct-browser.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_RunFenceAssociationLauncherCommand_ForDifferentAccount_AllowingAccountRedirection_IsAccepted()
    {
        SetUrlUserChoice(TestSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry
                {
                    Id = "browser02b",
                    AccountSid = "S-1-5-21-other",
                    ExePath = @"C:\Apps\other.exe",
                    AllowPassingArguments = true
                },
                new HandlerMappingEntry("browser02b")));

        using var result = CreateResolver().ResolveUrlHandler(
            BasicIdentityAllowingAccountRedirection(),
            "https://github.com/RunFence/");

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
        Assert.Equal(@"--resolve ""https"" ""https://github.com/RunFence/""", result.Target.Arguments);
    }

    [Fact]
    public void ResolveUrlTarget_RunFenceAssociationLauncherCommand_ForUrlApp_IsAccepted()
    {
        SetUrlUserChoice(TestSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");
        SetUserProtocol(TestSid, "https", @"""C:\Apps\direct-browser.exe"" ""%1""");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry { Id = "browser03", AccountSid = TestSid, ExePath = "https://example.com", IsUrlScheme = true },
                new HandlerMappingEntry("browser03")));

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/");

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
    }

    [Fact]
    public void ResolveUrlTarget_RunFenceAssociationLauncher_AppDisallowingArguments_IsAccepted()
    {
        SetUrlUserChoice(TestSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");
        SetUserProtocol(TestSid, "https", @"""C:\Apps\direct-browser.exe"" ""%1""");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                TestSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry
                {
                    Id = "browser06",
                    AccountSid = TestSid,
                    ExePath = @"C:\Apps\browser.exe",
                    AllowPassingArguments = false,
                    DefaultArguments = "--home"
                },
                new HandlerMappingEntry("browser06")));

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/");

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
    }

    [Fact]
    public void ResolveUrlTarget_InteractiveFallback_RunFenceAssociationLauncher_SameSid_IsAccepted()
    {
        SetUrlUserChoice(InteractiveSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");
        SetUserProtocol(InteractiveSid, "https", @"""C:\Apps\direct-browser.exe"" ""%1""");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                InteractiveSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry { Id = "browser04", AccountSid = InteractiveSid, ExePath = @"C:\Users\Test\AppData\Local\Browser\browser.exe" },
                new HandlerMappingEntry("browser04")));

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/");

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
    }

    [Fact]
    public void ResolveUrlTarget_InteractiveFallback_RunFenceAssociationLauncher_SameSid_ScriptExePath_IsAccepted()
    {
        SetUrlUserChoice(InteractiveSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");
        SetUserProtocol(InteractiveSid, "https", @"""C:\Apps\direct-browser.exe"" ""%1""");
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                InteractiveSid,
                true))
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry { Id = "browser05", AccountSid = InteractiveSid, ExePath = @"C:\Users\Test\AppData\Local\Browser\run.cmd" },
                new HandlerMappingEntry("browser05")));

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/");

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
    }

    [Fact]
    public void ResolveUrlTarget_UserProtocolRequiresUrlProtocolValue()
    {
        SetUserProtocol(TestSid, "myapp", @"""C:\Apps\broken.exe"" ""%1""", addUrlProtocol: false);
        SetMachineProtocol("myapp", @"""C:\Apps\machine.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "myapp://value");

        Assert.Equal("machine.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_MachineProtocolDelegateExecute_UsesShellUrlWrapper()
    {
        SetMachineProtocolDelegateExecute("ms-settings", "{4ed3a719-cea8-4bd9-910d-e252f997afc2}");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "ms-settings:defaultapps");

        Assert.Equal("rundll32.exe", result.Target.ExePath);
        Assert.Equal("url.dll,FileProtocolHandler ms-settings:defaultapps", result.Target.Arguments);
        Assert.Equal(LaunchResolutionKind.Handler, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_UserChoiceProgIdWithDelegateExecute_UsesShellUrlWrapper()
    {
        SetUrlUserChoice(TestSid, "ms-settings", "Windows.Settings");
        SetMachineProgIdDelegateExecute("Windows.Settings", "{4ed3a719-cea8-4bd9-910d-e252f997afc2}");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "ms-settings:defaultapps");

        Assert.Equal("rundll32.exe", result.Target.ExePath);
        Assert.Equal("url.dll,FileProtocolHandler ms-settings:defaultapps", result.Target.Arguments);
        Assert.Equal(LaunchResolutionKind.Handler, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_MachineProtocolCommandTakesPriorityOverDelegateExecute()
    {
        SetMachineProtocolCommandAndDelegateExecute(
            "ms-settings",
            @"""C:\Apps\settings-proxy.exe"" ""%1""",
            "{4ed3a719-cea8-4bd9-910d-e252f997afc2}");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "ms-settings:defaultapps");

        Assert.Equal("settings-proxy.exe", ExeName(result.Target));
        Assert.Equal(@"""ms-settings:defaultapps""", result.Target.Arguments);
        Assert.Equal(LaunchResolutionKind.Handler, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_MachineProtocolBrokenCommandFallsBackToDelegateExecute()
    {
        SetMachineProtocolCommandAndDelegateExecute(
            "ms-settings",
            @"""C:\Apps\broken.exe %1",
            "{4ed3a719-cea8-4bd9-910d-e252f997afc2}");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "ms-settings:defaultapps");

        Assert.Equal("rundll32.exe", result.Target.ExePath);
        Assert.Equal("url.dll,FileProtocolHandler ms-settings:defaultapps", result.Target.Arguments);
        Assert.Equal(LaunchResolutionKind.Handler, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_InteractiveFallback_ReusesResolutionPipeline()
    {
        SetUrlUserChoice(InteractiveSid, "zoommtg", "Interactive.Zoom");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Zoom", @"""C:\Apps\zoom.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "zoommtg://meeting");

        Assert.Equal("zoom.exe", ExeName(result.Target));
    }

    [Fact]
    public void ResolveUrlTarget_InteractiveFallback_KeepsBothHiveLeasesAliveUntilReturnedLeaseIsDisposed()
    {
        var launchedLease = new TrackingDisposable();
        var interactiveLease = new TrackingDisposable();
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(TestSid)).Returns(launchedLease);
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(InteractiveSid)).Returns(interactiveLease);

        SetUrlUserChoice(InteractiveSid, "zoommtg", "Interactive.Zoom");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Zoom", @"""C:\Apps\zoom.exe"" ""%1""");

        var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "zoommtg://meeting");

        Assert.Equal("zoom.exe", ExeName(result.Target));
        Assert.NotEqual(LaunchResolutionKind.ShellWrapped, result.Kind);
        Assert.False(launchedLease.IsDisposed);
        Assert.False(interactiveLease.IsDisposed);

        result.Dispose();

        Assert.True(launchedLease.WaitUntilDisposed());
        Assert.True(interactiveLease.WaitUntilDisposed());
    }

    [Fact]
    public void ResolveUrlTarget_InteractiveFallback_RejectsHandlersUnderUsers()
    {
        SetUrlUserChoice(InteractiveSid, "zoommtg", "Interactive.Zoom");
        SetUserProgIdCommand(InteractiveSid, "Interactive.Zoom", @"""C:\Users\Test\AppData\Local\Zoom\zoom.exe"" ""%1""");

        using var result = CreateResolver().ResolveUrlHandler(BasicIdentity(), "zoommtg://meeting");
        var legacyTarget = ProcessLaunchHelper.BuildUrlLaunchTarget("zoommtg://meeting");

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_NoUsableHandler_HighestAllowedThrows()
    {
        var resolver = CreateResolver();

        var ex = Assert.Throws<AssociationResolutionException>(() =>
            resolver.ResolveUrlHandler(HighestAllowedIdentity(), "steam://run/12345"));
        Assert.Equal("No usable association handler found for 'steam://run/12345'.", ex.Message);
    }

    [Fact]
    public void ResolveUrlTarget_NoUsableHandler_HighestAllowedAllowingAccountRedirection_FallsBackToLegacyWrappedTarget()
    {
        var resolver = CreateResolver();
        var legacyTarget = ProcessLaunchHelper.BuildUrlLaunchTarget("steam://run/12345");

        using var result = resolver.ResolveUrlHandler(
            HighestAllowedIdentityAllowingAccountRedirection(),
            "steam://run/12345");

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_NoUsableHandler_BasicFallsBackToLegacyWrappedTarget()
    {
        var resolver = CreateResolver();
        var legacyTarget = ProcessLaunchHelper.BuildUrlLaunchTarget("steam://run/12345");

        using var result = resolver.ResolveUrlHandler(BasicIdentity(), "steam://run/12345");

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_NoUsableHandler_HighIntegrityFallsBackToLegacyWrappedTarget()
    {
        var resolver = CreateResolver();
        var legacyTarget = ProcessLaunchHelper.BuildUrlLaunchTarget("steam://run/12345");

        using var result = resolver.ResolveUrlHandler(
            new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.HighIntegrity },
            "steam://run/12345");

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
    }

    [Fact]
    public void ResolveUrlTarget_NoUsableHandler_BasicFallback_KeepsLoadedHiveLeasesUntilReturnedLeaseIsDisposed()
    {
        var launchedLease = new TrackingDisposable();
        var interactiveLease = new TrackingDisposable();
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(TestSid)).Returns(launchedLease);
        _registry.HiveManager.Setup(h => h.EnsureHiveLoaded(InteractiveSid)).Returns(interactiveLease);

        var resolver = CreateResolver();
        var legacyTarget = ProcessLaunchHelper.BuildUrlLaunchTarget("steam://run/12345");

        var result = resolver.ResolveUrlHandler(BasicIdentity(), "steam://run/12345");

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
        Assert.False(launchedLease.IsDisposed);
        Assert.False(interactiveLease.IsDisposed);

        result.Dispose();

        Assert.True(launchedLease.WaitUntilDisposed());
        Assert.True(interactiveLease.WaitUntilDisposed());
    }

    [Fact]
    public void ResolveAssociation_AppContainerIdentity_ReturnsLegacyWrappedTargetWithoutHiveLoad()
    {
        var identity = new AppContainerLaunchIdentity(new AppContainerEntry
        {
            Name = "container",
            DisplayName = "Container",
            Sid = "S-1-15-2-100-200"
        });
        var originalTarget = FileTarget(@"C:\Docs\report.pdf");

        using var result = CreateResolver().ResolveFileHandler(identity, originalTarget);
        var legacyTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ResolveUrlTarget_AppContainerIdentity_ReturnsLegacyWrappedTargetWithoutHiveLoad()
    {
        var identity = new AppContainerLaunchIdentity(new AppContainerEntry
        {
            Name = "container",
            DisplayName = "Container",
            Sid = "S-1-15-2-100-200"
        });

        using var result = CreateResolver().ResolveUrlHandler(identity, "steam://run/12345");
        var legacyTarget = ProcessLaunchHelper.BuildUrlLaunchTarget("steam://run/12345");

        Assert.Equal(legacyTarget, result.Target);
        Assert.Equal(LaunchResolutionKind.ShellWrapped, result.Kind);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TraversePath_WorkerThread_UsesUiThreadSnapshotForManagedShortcutLookup()
    {
        var lnkPath = @"C:\Shortcuts\managed-worker.lnk";
        var app = new AppEntry { Id = "workerapp", ExePath = @"C:\Apps\worker.exe", AccountSid = TestSid };
        var liveDatabase = new AppDatabase();
        liveDatabase.Apps.Add(app);
        var session = new SessionContext
{
            Database = liveDatabase,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new Mock<ISessionProvider>();
        var sessionThreadId = 0;
        sessionProvider.Setup(s => s.GetSession())
            .Callback(() => sessionThreadId = Environment.CurrentManagedThreadId)
            .Returns(session);
        var shortcutGateway = new Mock<IShortcutGateway>();
        shortcutGateway.Setup(h => h.Read(lnkPath))
            .Returns(new ShortcutData(LauncherPath, app.Id, null, null, 0, null, 0, 1));

        using var uiInvoker = new DedicatedThreadUiInvoker();
        var resolver = CreateResolver(
            shortcutGateway: shortcutGateway.Object,
            uiThreadInvoker: uiInvoker,
            sessionProvider: sessionProvider.Object);

        var result = await Task.Run(() => resolver.TraversePath(lnkPath, BasicIdentity()));

        Assert.Equal(app.ExePath, result.TraversedPath);
        Assert.Equal(uiInvoker.ThreadId, sessionThreadId);
    }

    [Fact]
    public async Task ResolveUrlTarget_WorkerThread_UsesUiThreadSnapshotBeforeAssociationResolution()
    {
        SetUrlUserChoice(TestSid, "https", PathConstants.HandlerProgIdPrefix + "https");
        SetMachineProgIdCommand(PathConstants.HandlerProgIdPrefix + "https", $"\"{LauncherPath}\" --resolve \"https\" \"%1\"");

        var liveDatabase = new AppDatabase();
        liveDatabase.Apps.Add(new AppEntry { Id = "browser01", AccountSid = TestSid, ExePath = @"C:\Apps\browser.exe" });
        var session = new SessionContext
{
            Database = liveDatabase,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new Mock<ISessionProvider>();
        var sessionThreadId = 0;
        sessionProvider.Setup(s => s.GetSession())
            .Callback(() => sessionThreadId = Environment.CurrentManagedThreadId)
            .Returns(session);

        AppDatabase? resolvedDatabase = null;
        _associationLaunchResolver
            .Setup(r => r.Resolve(
                It.IsAny<AppDatabase>(),
                It.Is<AssociationLaunchRequest>(x => x.AssociationKey == "https" && x.RawArgument == @"""https://github.com/RunFence/"""),
                null,
                TestSid,
                true))
            .Callback<AppDatabase, AssociationLaunchRequest, string?, string?, bool>((db, _, _, _, _) => resolvedDatabase = db)
            .Returns(new AssociationLaunchResolution(
                AssociationLaunchResolutionStatus.Success,
                new AppEntry { Id = "browser01", AccountSid = TestSid, ExePath = @"C:\Apps\browser.exe" },
                new HandlerMappingEntry("browser01")));

        using var uiInvoker = new DedicatedThreadUiInvoker();
        var resolver = CreateResolver(uiThreadInvoker: uiInvoker, sessionProvider: sessionProvider.Object);

        using var result = await Task.Run(() => resolver.ResolveUrlHandler(BasicIdentity(), "https://github.com/RunFence/"));

        Assert.Equal(PathConstants.LauncherExeName, Path.GetFileName(result.Target.ExePath));
        Assert.Equal(uiInvoker.ThreadId, sessionThreadId);
        Assert.NotNull(resolvedDatabase);
        Assert.NotSame(liveDatabase, resolvedDatabase);
    }

    [Fact]
    public void TraversePath_FolderPath_ReturnsFolderKind()
    {
        var folderPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        var result = CreateResolver().TraversePath(folderPath, BasicIdentity());

        Assert.True(result.IsFolder);
        Assert.Equal(folderPath, result.TraversedPath);
    }

    [Fact]
    public void TraversePath_ExeFile_ReturnsPassthrough()
    {
        var exePath = @"C:\Apps\tool.exe";

        var result = CreateResolver().TraversePath(exePath, BasicIdentity());

        Assert.False(result.IsFolder);
        Assert.Equal(exePath, result.TraversedPath);
        Assert.Null(result.ShortcutArguments);
        Assert.Equal(".exe", result.Extension);
    }

    [Fact]
    public void TraversePath_LnkShortcutToNonManagedTarget_ResolvesTarget()
    {
        var lnkPath = @"C:\Shortcuts\notepad.lnk";
        var targetPath = @"C:\Windows\system32\notepad.exe";
        var shortcutGateway = new Mock<IShortcutGateway>();
        shortcutGateway.Setup(h => h.Read(lnkPath))
            .Returns(new ShortcutData(targetPath, null, null, null, 0, null, 0, 1));

        var result = CreateResolver(shortcutGateway.Object).TraversePath(lnkPath, BasicIdentity());

        Assert.False(result.IsFolder);
        Assert.Equal(targetPath, result.TraversedPath);
        Assert.Null(result.ShortcutArguments);
        Assert.Equal(".exe", result.Extension);
    }

    [Fact]
    public void TraversePath_LnkShortcutToManagedApp_ResolvesAppExePath()
    {
        var lnkPath = @"C:\Shortcuts\managed.lnk";
        var app = new AppEntry { Id = "app01", ExePath = @"C:\Apps\managed.exe", AccountSid = TestSid };
        _database.Apps.Add(app);
        var shortcutGateway = new Mock<IShortcutGateway>();
        shortcutGateway.Setup(h => h.Read(lnkPath))
            .Returns(new ShortcutData(LauncherPath, app.Id, null, null, 0, null, 0, 1));

        var result = CreateResolver(shortcutGateway.Object).TraversePath(lnkPath, BasicIdentity());

        Assert.False(result.IsFolder);
        Assert.Equal(app.ExePath, result.TraversedPath);
    }

    [Fact]
    public void TraversePath_LnkShortcutToFolderTarget_ReturnsFolderKind()
    {
        var lnkPath = @"C:\Shortcuts\myfolder.lnk";
        var folderPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var shortcutGateway = new Mock<IShortcutGateway>();
        shortcutGateway.Setup(h => h.Read(lnkPath))
            .Returns(new ShortcutData(folderPath, null, null, null, 0, null, 0, 1));

        var result = CreateResolver(shortcutGateway.Object).TraversePath(lnkPath, BasicIdentity());

        Assert.True(result.IsFolder);
        Assert.Equal(folderPath, result.TraversedPath);
    }

    [Fact]
    public void TraversePath_BrokenLnkShortcut_ThrowsInvalidOperationException()
    {
        var lnkPath = @"C:\Shortcuts\broken.lnk";
        var shortcutGateway = new Mock<IShortcutGateway>();
        shortcutGateway.Setup(h => h.Read(lnkPath))
            .Returns(new ShortcutData(string.Empty, null, null, null, 0, null, 0, 1));

        Assert.Throws<InvalidOperationException>(() =>
            CreateResolver(shortcutGateway.Object).TraversePath(lnkPath, BasicIdentity()));
    }

    [Fact]
    public void TraversePath_LnkShortcutArgs_ReturnsShortcutArguments()
    {
        var lnkPath = @"C:\Shortcuts\script.lnk";
        var targetPath = @"C:\Scripts\run.exe";
        var shortcutGateway = new Mock<IShortcutGateway>();
        shortcutGateway.Setup(h => h.Read(lnkPath))
            .Returns(new ShortcutData(targetPath, "--from-lnk", null, null, 0, null, 0, 1));

        var result = CreateResolver(shortcutGateway.Object).TraversePath(lnkPath, BasicIdentity());

        Assert.Equal("--from-lnk", result.ShortcutArguments);
    }

}

file sealed class TestHklmClassesRootProvider(InMemoryRegistryKey classesRoot) : IHklmClassesRootProvider
{
    public IRegistryKey OpenClassesRoot()
    {
        using var _ = classesRoot.CreateSubKey(@"Software\Classes");
        return classesRoot.OpenSubKey(@"Software\Classes")!;
    }
}

file sealed class TestHkuRootProvider(InMemoryRegistryKey usersRoot) : IHkuRootProvider
{
    public IRegistryKey OpenUsersRoot() => usersRoot;
}

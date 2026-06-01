using System.ComponentModel;
using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Launching.Resolution;
using RunFence.Security;
using LaunchProcessInfo = RunFence.Launch.Tokens.ProcessInfo;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="ProcessLauncher.Accept(AccountLaunchIdentity, ProcessLaunchTarget)"/> credential resolution behavior.
/// </summary>
public class ProcessLauncherTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IAccountProcessLauncher> _accountLauncher = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<IProfileRepairHelper> _profileRepair = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<IByteArrayCredentialEncryptionService> _encryptionService = new();
    private readonly Mock<IAppContainerProcessLauncher> _containerLauncher = new();
    private readonly Mock<IWindowsAppsAliasPathResolver> _windowsAppsAliasPathResolver = new();
    private readonly Mock<IExecutableKindService> _executableKindService = new();
    private readonly Mock<IWindowsAppsActivationLauncher> _windowsAppsActivationLauncher = new();
    private readonly ILaunchCredentialsLookup _credentialsLookup;

    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly SecureSecret _protectedPinKey;

    private readonly ProcessLauncher _processLauncher;

    private static LaunchProcessInfo MakeProcessInfo()
        => TestProcessInfoFactory.Native(new ProcessLaunchNative.PROCESS_INFORMATION());

    private static LaunchProcessInfo MakeLaunchResult()
        => MakeProcessInfo();

    public ProcessLauncherTests()
    {
        _protectedPinKey = TestSecretFactory.FromBytes(_pinDerivedKey);

        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithClonedPinDerivedKey(_protectedPinKey));

        var credentialDecryption = new CredentialDecryptionService(
            new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
            _sidResolver.Object,
            _interactiveUserSidResolver.Object);
        _credentialsLookup = new LaunchCredentialsLookup(
            _sessionProvider.Object,
            credentialDecryption,
            () => new InlineUiThreadInvoker(action => action()));

        _profileRepair
            .Setup(p => p.ExecuteWithProfileRepair(It.IsAny<Func<LaunchProcessInfo?>>(), It.IsAny<string?>()))
            .Returns<Func<LaunchProcessInfo?>, string?>((action, _) => action());

        _accountLauncher
            .Setup(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Returns(MakeLaunchResult);

        _processLauncher = new ProcessLauncher(
            _accountLauncher.Object,
            _credentialsLookup,
            _profileRepair.Object,
            _containerLauncher.Object,
            _windowsAppsAliasPathResolver.Object,
            _executableKindService.Object,
            _windowsAppsActivationLauncher.Object,
            _log.Object);
    }

    public void Dispose() => _protectedPinKey.Dispose();

    private void SetupStoredPassword(string sid, ProtectedString password)
    {
        var encryptedBytes = new byte[] { 1, 2, 3 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = sid,
            EncryptedPassword = encryptedBytes
        });
        _encryptionService.Setup(e => e.Decrypt(encryptedBytes, _pinDerivedKey)).Returns(password);
        _sidResolver.Setup(s => s.TryResolveName(sid)).Returns("DOMAIN\\testuser");
    }

    [Fact]
    public void Accept_NullCredentials_ResolvesViaLookup()
    {
        var password = new ProtectedString();
        password.AppendChar('p');
        SetupStoredPassword(TestSid, password);

        var identity = new AccountLaunchIdentity(TestSid)
        {
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        _processLauncher.Accept(identity, target);

        _accountLauncher.Verify(a => a.Launch(
            target,
            It.Is<AccountLaunchIdentity>(id =>
                id.Credentials != null &&
                id.Credentials.Value.TokenSource == LaunchTokenSource.Credentials)),
            Times.Once);
    }

    [Fact]
    public void Accept_CallerProvidedCredentials_SkipsLookup()
    {
        using var callerPassword = new ProtectedString();
        callerPassword.AppendChar('x');

        var callerCreds = new LaunchCredentials(callerPassword, "DOMAIN", "user");
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = callerCreds,
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        _processLauncher.Accept(identity, target);

        _accountLauncher.Verify(a => a.Launch(
            target,
            It.Is<AccountLaunchIdentity>(id =>
                id.Credentials != null &&
                id.Credentials.Value.Password == callerPassword)),
            Times.Once);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Accept_LookedUpPassword_DisposedAfterLaunch()
    {
        var password = new ProtectedString();
        password.AppendChar('s');
        SetupStoredPassword(TestSid, password);

        var identity = new AccountLaunchIdentity(TestSid)
        {
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        _processLauncher.Accept(identity, target);

        Assert.Throws<ObjectDisposedException>(() => password.AppendChar('x'));
    }

    [Fact]
    public void Accept_CallerPassword_NotDisposed()
    {
        var callerPassword = new ProtectedString();
        callerPassword.AppendChar('y');

        var callerCreds = new LaunchCredentials(callerPassword, "DOMAIN", "user");
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = callerCreds,
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        _processLauncher.Accept(identity, target);

        callerPassword.AppendChar('z');
        callerPassword.Dispose();
    }

    [Fact]
    public void Launch_AppContainerIdentity_DelegatesToContainerLauncher()
    {
        var container = new AppContainerEntry { Name = "rfn_test", Sid = "S-1-15-2-1-2-3-4" };
        var identity = new AppContainerLaunchIdentity(container);
        var target = new ProcessLaunchTarget(@"C:\apps\browser.exe");
        var expectedInfo = MakeProcessInfo();
        _containerLauncher.Setup(c => c.LaunchFile(target, identity)).Returns(expectedInfo);

        var result = _processLauncher.Launch(identity, target);

        Assert.Same(expectedInfo, result);
        _containerLauncher.Verify(c => c.LaunchFile(target, identity), Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(),
            It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Launch_AppContainerIdentity_PreservesSuppressStartupFeedback()
    {
        var container = new AppContainerEntry { Name = "rfn_test", Sid = "S-1-15-2-1-2-3-4" };
        var identity = new AppContainerLaunchIdentity(container);
        var target = new ProcessLaunchTarget(@"C:\apps\browser.exe", SuppressStartupFeedback: true);
        var expectedInfo = MakeProcessInfo();
        _containerLauncher.Setup(c => c.LaunchFile(target, identity)).Returns(expectedInfo);

        var result = _processLauncher.Launch(identity, target);

        Assert.Same(expectedInfo, result);
        _containerLauncher.Verify(c => c.LaunchFile(target, identity), Times.Once);
    }

    [Fact]
    public void Launch_AppContainerIdentity_DoesNotApplyAutomaticStartupFeedbackSuppression()
    {
        var container = new AppContainerEntry { Name = "rfn_test", Sid = "S-1-15-2-1-2-3-4" };
        var identity = new AppContainerLaunchIdentity(container);
        var target = new ProcessLaunchTarget(@"C:\Program Files\WindowsApps\Pkg\App.exe");
        var expectedInfo = MakeProcessInfo();
        _containerLauncher.Setup(c => c.LaunchFile(target, identity)).Returns(expectedInfo);

        var result = _processLauncher.Launch(identity, target);

        Assert.Same(expectedInfo, result);
        _containerLauncher.Verify(c => c.LaunchFile(target, identity), Times.Once);
    }

    [Fact]
    public void Accept_AccountIdentity_InteractiveUserTokenSource_DoesNotDisposeCallerPassword()
    {
        var callerPassword = new ProtectedString();
        callerPassword.AppendChar('i');
        var callerCreds = new LaunchCredentials(callerPassword, null, null, LaunchTokenSource.InteractiveUser);
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = callerCreds,
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        _processLauncher.Accept(identity, target);

        callerPassword.AppendChar('j');
        callerPassword.Dispose();
    }

    [Fact]
    public void Accept_WindowsAppsPackagePath_UsesActivationLauncherWithStartupFeedbackSuppressed()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(
            @"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe");
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _windowsAppsActivationLauncher
            .Setup(l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns((LaunchProcessInfo?)null);

        var result = _processLauncher.Accept(identity, target);

        Assert.Null(result);
        _windowsAppsActivationLauncher.Verify(
            l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Accept_WindowsAppsAliasForTargetUser_UsesActivationLauncherWithStartupFeedbackSuppressed()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget("notepad.exe");
        const string resolvedAliasPath = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\notepad.exe";
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _windowsAppsAliasPathResolver
            .Setup(r => r.TryResolveForUserSid(target.ExePath, identity.Sid))
            .Returns(resolvedAliasPath);
        _windowsAppsActivationLauncher
            .Setup(l => l.TryLaunch(
                launchedTarget,
                resolvedAliasPath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns((LaunchProcessInfo?)null);

        var result = _processLauncher.Accept(identity, target);

        Assert.Null(result);
        _windowsAppsActivationLauncher.Verify(
            l => l.TryLaunch(
                launchedTarget,
                resolvedAliasPath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Accept_WindowsShimUwpTarget_UsesActivationLauncherWithStartupFeedbackSuppressed()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\Windows\notepad.exe");
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _executableKindService
            .Setup(s => s.IsUwpExeFile(target.ExePath))
            .Returns(true);
        _windowsAppsActivationLauncher
            .Setup(l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns((LaunchProcessInfo?)null);

        var result = _processLauncher.Accept(identity, target);

        Assert.Null(result);
        _windowsAppsActivationLauncher.Verify(
            l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Accept_RootedNonWindowsAppsPath_DoesNotUseAliasNameFallback()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\Tools\notepad.exe");

        _processLauncher.Accept(identity, target);

        _windowsAppsAliasPathResolver.Verify(
            r => r.TryResolveForUserSid(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _accountLauncher.Verify(a => a.Launch(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == target.ExePath
                && t.Arguments == target.Arguments
                && !t.SuppressStartupFeedback),
            It.IsAny<AccountLaunchIdentity>()), Times.Once);
    }

    [Fact]
    public void Accept_WindowsAppsPackagePath_UsesActivationLauncherWithoutDirectLaunch()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(NotepadPackageExe(), "--resume abc");
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _windowsAppsActivationLauncher
            .Setup(l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns((LaunchProcessInfo?)null);

        var result = _processLauncher.Accept(identity, target);

        Assert.Null(result);
        _windowsAppsActivationLauncher.Verify(
            l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Accept_WindowsAppsAliasForTargetUser_UsesActivationLauncherWithOriginalAliasTarget()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget("notepad.exe", "--resume abc");
        const string resolvedAliasPath = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\notepad.exe";
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _windowsAppsAliasPathResolver
            .Setup(r => r.TryResolveForUserSid(target.ExePath, identity.Sid))
            .Returns(resolvedAliasPath);
        _windowsAppsActivationLauncher
            .Setup(l => l.TryLaunch(
                launchedTarget,
                resolvedAliasPath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns((LaunchProcessInfo?)null);

        var result = _processLauncher.Accept(identity, target);

        Assert.Null(result);
        _windowsAppsActivationLauncher.Verify(
            l => l.TryLaunch(
                launchedTarget,
                resolvedAliasPath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Accept_BareWindowsAppsAlias_ActivationFactoryReceivesResolvedAliasPath()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget("wt.exe", "--new-tab");
        const string resolvedAliasPath = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe";
        var targetFactory = new Mock<IWindowsAppsActivationTargetFactory>();
        var helperLauncher = new Mock<IWindowsAppsActivationHelperLauncher>(MockBehavior.Strict);
        var mandatoryLabelService = new Mock<IMandatoryLabelService>(MockBehavior.Strict);
        var activationLauncher = new WindowsAppsActivationLauncher(
            targetFactory.Object,
            helperLauncher.Object,
            new Mock<IWindowsAppsActivationResultPoller>(MockBehavior.Strict).Object,
            mandatoryLabelService.Object);
        var processLauncher = new ProcessLauncher(
            _accountLauncher.Object,
            _credentialsLookup,
            _profileRepair.Object,
            _containerLauncher.Object,
            _windowsAppsAliasPathResolver.Object,
            _executableKindService.Object,
            activationLauncher,
            _log.Object);
        _windowsAppsAliasPathResolver
            .Setup(r => r.TryResolveForUserSid(target.ExePath, identity.Sid))
            .Returns(resolvedAliasPath);
        targetFactory
            .Setup(f => f.TryCreate(
                It.Is<ProcessLaunchTarget>(t => t.ExePath == target.ExePath && t.Arguments == target.Arguments),
                resolvedAliasPath,
                identity.Sid))
            .Returns((WindowsAppsActivationTarget?)null);

        Assert.Throws<InvalidOperationException>(() => processLauncher.Accept(identity, target));

        targetFactory.Verify(f => f.TryCreate(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == target.ExePath
                && t.Arguments == target.Arguments
                && t.SuppressStartupFeedback),
            resolvedAliasPath,
            identity.Sid), Times.Once);
        helperLauncher.Verify(
            l => l.Launch(It.IsAny<WindowsAppsActivationTarget>(), It.IsAny<AccountLaunchIdentity>(), It.IsAny<AccountLaunchIdentity>()),
            Times.Never);
    }

    [Fact]
    public void Accept_WindowsAppsTargetWithNoProcessResult_DoesNotDirectLaunch()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(NotepadPackageExe(), "--resume abc");
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _windowsAppsActivationLauncher
            .Setup(l => l.TryLaunch(
                launchedTarget,
                target.ExePath,
                identity,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns((LaunchProcessInfo?)null);

        var result = _processLauncher.Accept(identity, target);

        Assert.Null(result);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    private static string NotepadPackageExe() =>
        Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");
}

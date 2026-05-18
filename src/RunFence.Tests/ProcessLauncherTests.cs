using System.ComponentModel;
using Moq;
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
    private readonly Mock<IWindowsAppsRegistrationRepairRunner> _windowsAppsRegistrationRepairRunner = new();

    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly SecureSecret _protectedPinKey;

    private readonly ProcessLauncher _processLauncher;

    private static LaunchProcessInfo MakeProcessInfo()
        => new(new ProcessLaunchNative.PROCESS_INFORMATION());

    public ProcessLauncherTests()
    {
        _protectedPinKey = TestSecretFactory.FromBytes(_pinDerivedKey);

        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithOwnedPinDerivedKey(_protectedPinKey));

        var credentialDecryption = new CredentialDecryptionService(
            new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
            _sidResolver.Object,
            _interactiveUserSidResolver.Object);
        var credentialsLookup = new LaunchCredentialsLookup(
            _sessionProvider.Object,
            credentialDecryption,
            () => new InlineUiThreadInvoker(action => action()));

        _profileRepair
            .Setup(p => p.ExecuteWithProfileRepair(It.IsAny<Func<LaunchProcessInfo?>>(), It.IsAny<string?>()))
            .Returns<Func<LaunchProcessInfo?>, string?>((action, _) => action());

        _accountLauncher
            .Setup(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Returns(MakeProcessInfo);

        _processLauncher = new ProcessLauncher(
            _accountLauncher.Object,
            credentialsLookup,
            _profileRepair.Object,
            _containerLauncher.Object,
            _windowsAppsAliasPathResolver.Object,
            _executableKindService.Object,
            _windowsAppsRegistrationRepairRunner.Object,
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

        Assert.Equal(expectedInfo, result);
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

        Assert.Equal(expectedInfo, result);
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

        Assert.Equal(expectedInfo, result);
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
    public void Accept_WindowsAppsPackagePath_SuppressesStartupFeedbackOnLaunchedTarget()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(
            @"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe");

        _processLauncher.Accept(identity, target);

        _accountLauncher.Verify(a => a.Launch(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == target.ExePath
                && t.Arguments == target.Arguments
                && t.SuppressStartupFeedback),
            It.IsAny<AccountLaunchIdentity>()), Times.Once);
    }

    [Fact]
    public void Accept_WindowsAppsAliasForTargetUser_SuppressesStartupFeedbackOnLaunchedTarget()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget("notepad.exe");
        _windowsAppsAliasPathResolver
            .Setup(r => r.TryResolveForUserSid(target.ExePath, identity.Sid))
            .Returns(@"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\notepad.exe");

        _processLauncher.Accept(identity, target);

        _accountLauncher.Verify(a => a.Launch(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == target.ExePath
                && t.Arguments == target.Arguments
                && t.SuppressStartupFeedback),
            It.IsAny<AccountLaunchIdentity>()), Times.Once);
    }

    [Fact]
    public void Accept_WindowsShimUwpTarget_SuppressesStartupFeedbackOnLaunchedTarget()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(@"C:\Windows\notepad.exe");
        _executableKindService
            .Setup(s => s.IsUwpExeFile(target.ExePath))
            .Returns(true);

        _processLauncher.Accept(identity, target);

        _accountLauncher.Verify(a => a.Launch(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == target.ExePath
                && t.Arguments == target.Arguments
                && t.SuppressStartupFeedback),
            It.IsAny<AccountLaunchIdentity>()), Times.Once);
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
    public void Accept_WindowsAppsAccessDenied_RunnerRepairsAndRetries()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(NotepadPackageExe(), @"C:\Users\a\AppData\Local\RunFence\runfence.log");
        var launchedTarget = target with { SuppressStartupFeedback = true };
        var processInfo = MakeProcessInfo();
        _accountLauncher
            .SetupSequence(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Throws(new Win32Exception(5))
            .Returns(processInfo);
        _windowsAppsRegistrationRepairRunner
            .Setup(r => r.TryRepair(
                launchedTarget,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns(true);

        var result = _processLauncher.Accept(identity, target);

        Assert.Same(processInfo, result);
        _windowsAppsRegistrationRepairRunner.Verify(r => r.TryRepair(
            launchedTarget,
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(launchedTarget, It.IsAny<AccountLaunchIdentity>()), Times.Exactly(2));
    }

    [Fact]
    public void Accept_JobKeeperAccessDenied_RunnerRepairsAndRetries()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(NotepadPackageExe(), @"C:\Users\a\AppData\Local\RunFence\runfence.log");
        var launchedTarget = target with { SuppressStartupFeedback = true };
        var processInfo = MakeProcessInfo();
        _accountLauncher
            .SetupSequence(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Throws(new JobKeeperChildLaunchException("access denied", 5))
            .Returns(processInfo);
        _windowsAppsRegistrationRepairRunner
            .Setup(r => r.TryRepair(
                launchedTarget,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns(true);

        var result = _processLauncher.Accept(identity, target);

        Assert.Same(processInfo, result);
        _windowsAppsRegistrationRepairRunner.Verify(r => r.TryRepair(
            launchedTarget,
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(launchedTarget, It.IsAny<AccountLaunchIdentity>()), Times.Exactly(2));
    }

    [Fact]
    public void Accept_AppAliasAccessDenied_RunnerRepairsAndRetriesOriginalAliasTarget()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget("notepad.exe", @"C:\Users\a\AppData\Local\RunFence\runfence.log");
        var processInfo = MakeProcessInfo();
        _accountLauncher
            .SetupSequence(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Throws(new Win32Exception(5))
            .Returns(processInfo);
        _windowsAppsRegistrationRepairRunner
            .Setup(r => r.TryRepair(
                target,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns(true);

        var result = _processLauncher.Accept(identity, target);

        Assert.Same(processInfo, result);
        _windowsAppsRegistrationRepairRunner.Verify(r => r.TryRepair(
            target,
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(target, It.IsAny<AccountLaunchIdentity>()), Times.Exactly(2));
    }

    [Fact]
    public void Accept_AccessDeniedRepairRunnerReturnsFalse_PropagatesOriginalError()
    {
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            PrivilegeLevel = PrivilegeLevel.Isolated
        };
        var target = new ProcessLaunchTarget(NotepadPackageExe());
        var launchedTarget = target with { SuppressStartupFeedback = true };
        _accountLauncher
            .SetupSequence(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Throws(new Win32Exception(5));
        _windowsAppsRegistrationRepairRunner
            .Setup(r => r.TryRepair(
                launchedTarget,
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
                It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)))
            .Returns(false);

        Assert.Throws<Win32Exception>(() => _processLauncher.Accept(identity, target));

        _windowsAppsRegistrationRepairRunner.Verify(r => r.TryRepair(
            launchedTarget,
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials == identity.Credentials),
            It.Is<AccountLaunchIdentity>(id => id.Sid == identity.Sid && id.Credentials != null)),
            Times.Once);
        _accountLauncher.Verify(a => a.Launch(launchedTarget, It.IsAny<AccountLaunchIdentity>()), Times.Once);
    }

    private static string NotepadPackageExe() =>
        Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");
}

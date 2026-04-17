using RunFence.Launch.Tokens;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeServiceTests : IDisposable
{
    private readonly Mock<IGlobalHotkeyService> _hotkeyService;
    private readonly Mock<IWindowOwnerDetector> _windowOwnerDetector;
    private readonly Mock<IDragBridgeLauncher> _launcher;
    private readonly Mock<INotificationService> _notifications;
    private readonly Mock<ILoggingService> _log;
    private readonly DragBridgeService _service;
    private readonly ProtectedBuffer _pinKey;

    private static readonly string CurrentSid = SidResolutionHelper.GetCurrentUserSid();
    private static readonly SecurityIdentifier CurrentSecId = new(CurrentSid);

    public DragBridgeServiceTests()
    {
        _hotkeyService = new Mock<IGlobalHotkeyService>();
        _hotkeyService.Setup(h => h.Register(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>())).Returns(true);
        _windowOwnerDetector = new Mock<IWindowOwnerDetector>();
        _launcher = new Mock<IDragBridgeLauncher>();
        _notifications = new Mock<INotificationService>();
        _log = new Mock<ILoggingService>();

        var aclPermission = new Mock<IAclPermissionService>();
        var permissionGrant = new Mock<IPathGrantService>();
        var sessionSaver = new Mock<ISessionSaver>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(u => u.Invoke(It.IsAny<Action>())).Callback<Action>(a => a());
        var tempManager = new Mock<IDragBridgeTempFileManager>();

        var processLauncher = new DragBridgeProcessLauncher(
            _launcher.Object, _log.Object, uiThreadInvoker.Object,
            dragBridgeExePath: "fake.exe");
        var copyFlow = new DragBridgeCopyFlow(processLauncher, _notifications.Object, _log.Object,
            new CapturedFileStore(), pipeConnectTimeoutMs: 100);
        var pasteHandler = new DragBridgePasteHandler(
            NullDragBridgeAccessPrompt.Instance,
            tempManager.Object,
            _notifications.Object,
            _log.Object,
            uiThreadInvoker.Object,
            aclPermission.Object,
            permissionGrant.Object,
            new SidDisplayNameResolver(new Mock<ISidResolver>().Object));
        var resolveOrchestrator = new DragBridgeResolveOrchestrator(
            pasteHandler,
            sessionSaver.Object,
            new Mock<IQuickAccessPinService>().Object,
            uiThreadInvoker.Object);

        _service = new DragBridgeService(
            _hotkeyService.Object,
            _windowOwnerDetector.Object,
            _notifications.Object,
            _log.Object,
            tempManager.Object,
            processLauncher,
            copyFlow,
            resolveOrchestrator);
        _service.Initialize();

        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _service.SetData(new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        });
    }

    public void Dispose()
    {
        _service.Dispose();
        _pinKey.Dispose();
    }

    // --- Hotkey encoding ---

    [Theory]
    [InlineData(0x60043, 0x0003, 0x43)] // Ctrl+Alt+C → MOD_CONTROL|MOD_ALT, VK=C
    [InlineData(0x60056, 0x0003, 0x56)] // Ctrl+Alt+V → MOD_CONTROL|MOD_ALT, VK=V
    [InlineData(0x20043, 0x0002, 0x43)] // Ctrl+C → MOD_CONTROL, VK=C
    [InlineData(0x10043, 0x0004, 0x43)] // Shift+C → MOD_SHIFT, VK=C
    [InlineData(0xE0051, 0x000B, 0x51)] // Win+Ctrl+Alt+Q → MOD_WIN|MOD_CONTROL|MOD_ALT, VK=Q
    [InlineData(0x80051, 0x0008, 0x51)] // Win+Q → MOD_WIN, VK=Q
    public void SplitModifiers_CorrectlyMaps_KeysModifierToHotkeyMod(int keysValue, int expectedMods, int expectedVk)
    {
        Assert.Equal(expectedMods, DragBridgeHotkeyHelper.SplitModifiers(keysValue));
        Assert.Equal(expectedVk, DragBridgeHotkeyHelper.GetVirtualKey(keysValue));
    }

    // --- ApplySettings ---

    [Fact]
    public void ApplySettings_Enabled_RegistersSingleHotkey()
    {
        _service.ApplySettings(new AppSettings
        {
            EnableDragBridge = true,
            DragBridgeCopyHotkey = 0x60043,
        });

        _hotkeyService.Verify(h => h.Register(DragBridgeService.CopyHotkeyId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Once);
        _hotkeyService.Verify(h => h.Register(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void ApplySettings_Disabled_UnregistersAll()
    {
        _service.ApplySettings(new AppSettings { EnableDragBridge = false });

        _hotkeyService.Verify(h => h.UnregisterAll(), Times.Once);
        _hotkeyService.Verify(h => h.Register(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
    }

    // --- Window owner resolution ---

    [Fact]
    public void CopyHotkey_OwnerUnresolvable_ShowsWarning()
    {
        _service.ApplySettings(new AppSettings { EnableDragBridge = true });
        _windowOwnerDetector.Setup(d => d.GetDragSourceOrForegroundOwnerInfo()).Returns((WindowOwnerInfo?)null);

        _hotkeyService.Raise(h => h.HotkeyPressed += null, DragBridgeService.CopyHotkeyId);

        _notifications.Verify(n => n.ShowWarning(It.IsAny<string>(), It.Is<string>(s => s.Contains("Cannot identify"))), Times.Once);
    }

    // --- LaunchForSid dispatch (via hotkey) ---

    [Fact]
    public async Task CopyHotkey_UnknownSid_ShowsNoCredentialsWarning()
    {
        // Arrange
        var unknownSid = new SecurityIdentifier("S-1-5-21-0-0-0-9999");
        _windowOwnerDetector.Setup(d => d.GetDragSourceOrForegroundOwnerInfo())
            .Returns(new WindowOwnerInfo(unknownSid, NativeTokenHelper.MandatoryLevelHigh));

        var tcs = new TaskCompletionSource();
        _notifications.Setup(n => n.ShowWarning(It.IsAny<string>(),
                It.Is<string>(s => s.Contains("No credentials"))))
            .Callback(() => tcs.TrySetResult());

        // Act
        _service.ApplySettings(new AppSettings { EnableDragBridge = true });
        _hotkeyService.Raise(h => h.HotkeyPressed += null, DragBridgeService.CopyHotkeyId);

        // Assert — 5s timeout is not a sleep; it waits for the async completion signal from the mock
        // callback. The test fails (TimeoutException) if the notification is never raised, not flaky.
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _notifications.Verify(n => n.ShowWarning(It.IsAny<string>(),
            It.Is<string>(s => s.Contains("No credentials"))), Times.Once);
    }

    [Theory]
    [InlineData(NativeTokenHelper.MandatoryLevelHigh, PrivilegeLevel.HighestAllowed)]
    [InlineData(NativeTokenHelper.MandatoryLevelMedium, PrivilegeLevel.Basic)]
    [InlineData(NativeTokenHelper.MandatoryLevelLow, PrivilegeLevel.LowIntegrity)]
    public async Task CopyHotkey_CurrentUser_CallsLaunchDirectWithExpectedPrivilegeLevel(
        int integrityLevel, PrivilegeLevel expectedMode)
    {
        // Arrange
        _windowOwnerDetector.Setup(d => d.GetDragSourceOrForegroundOwnerInfo())
            .Returns(new WindowOwnerInfo(CurrentSecId, integrityLevel));

        var tcs = new TaskCompletionSource();
        _launcher.Setup(l => l.LaunchDirect(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                expectedMode))
            .Callback(() => tcs.TrySetResult())
            .Returns((ProcessInfo?)null);

        // Act
        _service.ApplySettings(new AppSettings { EnableDragBridge = true });
        _hotkeyService.Raise(h => h.HotkeyPressed += null, DragBridgeService.CopyHotkeyId);

        // Assert — 5s timeout waits for async completion, not a fixed sleep delay
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _launcher.Verify(l => l.LaunchDirect(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
            expectedMode), Times.Once);
    }

    // --- Bridge flow: managed launch ---

    [Fact]
    public async Task CopyHotkey_ManagedAccountWithCredential_CallsLaunchManagedNotLaunchDirect()
    {
        // For a SID with a stored credential (not current user, not interactive user),
        // the service must call LaunchManaged (not LaunchDirect) so the bridge process
        // runs under the target account's credentials.
        const string managedSid = "S-1-5-21-9999-9999-9999-1500";
        var managedSecId = new SecurityIdentifier(managedSid);

        var sessionWithCredential = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore
            {
                Credentials = [new CredentialEntry { Sid = managedSid, EncryptedPassword = [1] }]
            },
            PinDerivedKey = _pinKey
        };
        _service.SetData(sessionWithCredential);
        _service.ApplySettings(new AppSettings { EnableDragBridge = true });

        _windowOwnerDetector.Setup(d => d.GetDragSourceOrForegroundOwnerInfo())
            .Returns(new WindowOwnerInfo(managedSecId, NativeTokenHelper.MandatoryLevelHigh));

        var launchCalledTcs = new TaskCompletionSource();
        _launcher.Setup(l => l.LaunchManaged(It.IsAny<string>(), managedSid, It.IsAny<IReadOnlyList<string>>()))
            .Callback(() => launchCalledTcs.TrySetResult())
            .Throws(new InvalidOperationException("test: no real process available"));

        _hotkeyService.Raise(h => h.HotkeyPressed += null, DragBridgeService.CopyHotkeyId);
        // 5s timeout waits for async completion, not a fixed sleep delay
        await launchCalledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _launcher.Verify(l => l.LaunchManaged(It.IsAny<string>(), managedSid, It.IsAny<IReadOnlyList<string>>()), Times.Once);
        _launcher.Verify(l => l.LaunchDirect(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<PrivilegeLevel>()), Times.Never);
        _launcher.Verify(l => l.LaunchDeElevated(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<PrivilegeLevel?>()), Times.Never);
    }

    [Fact]
    public async Task CopyHotkey_ManagedAccount_LaunchManagedThrows_ShowsError()
    {
        // When LaunchManaged throws (e.g. credential decryption fails), the service must
        // show an error notification via the UI-thread marshal callback.
        const string managedSid = "S-1-5-21-9999-9999-9999-1700";
        var managedSecId = new SecurityIdentifier(managedSid);

        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore
            {
                Credentials = [new CredentialEntry { Sid = managedSid, EncryptedPassword = [1] }]
            },
            PinDerivedKey = _pinKey
        };
        _service.SetData(session);
        _service.ApplySettings(new AppSettings { EnableDragBridge = true });

        _windowOwnerDetector.Setup(d => d.GetDragSourceOrForegroundOwnerInfo())
            .Returns(new WindowOwnerInfo(managedSecId, NativeTokenHelper.MandatoryLevelHigh));

        _launcher.Setup(l => l.LaunchManaged(It.IsAny<string>(), managedSid, It.IsAny<IReadOnlyList<string>>()))
            .Throws(new InvalidOperationException("Credential decryption failed"));

        var errorTcs = new TaskCompletionSource();
        _notifications.Setup(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => errorTcs.TrySetResult());

        // Act
        _hotkeyService.Raise(h => h.HotkeyPressed += null, DragBridgeService.CopyHotkeyId);

        // 5s timeout waits for async completion, not a fixed sleep delay
        await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — an error notification was shown when LaunchManaged threw
        _notifications.Verify(n => n.ShowError(It.IsAny<string>(),
            It.Is<string>(s => s.Contains("Failed to launch"))), Times.Once);
        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("managed launch failed")),
            It.IsAny<Exception>()), Times.Once);
    }

    // --- Dispose unregisters ---

    [Fact]
    public void Dispose_UnregistersAllHotkeys()
    {
        _service.Dispose();

        _hotkeyService.Verify(h => h.UnregisterAll(), Times.AtLeastOnce);
    }
}
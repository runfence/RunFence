using System.Security;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountPasswordHandlerTests : IDisposable
{
    private readonly Mock<IModalCoordinator> _modalCoordinator = new();
    private readonly Mock<IAccountCredentialManager> _credentialManager = new();
    private readonly Mock<IAccountPasswordService> _accountPassword = new();
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IWindowsHelloService> _windowsHello = new();
    private readonly Mock<IPasswordAutoTyper> _autoTyper = new();
    private readonly Mock<ISecureDesktopRunner> _secureDesktop = new();
    private readonly Mock<IEvaluationLimitHelper> _evaluationLimitHelper = new();

    private readonly AppDatabase _database = new();
    private readonly ProtectedBuffer _pinKey;
    private readonly SessionContext _session;
    private readonly CredentialStore _store;
    private readonly OperationGuard _guard = new();
    private readonly Panel _parent = new();
    private readonly SecureString _decryptedPwd;

    public AccountPasswordHandlerTests()
    {
        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _store = new CredentialStore();
        _session = new SessionContext
        {
            Database = _database,
            CredentialStore = _store,
            PinDerivedKey = _pinKey
        };
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;

        _decryptedPwd = new SecureString();
        _decryptedPwd.AppendChar('p');
        _decryptedPwd.MakeReadOnly();

        SecureString? outPwd = _decryptedPwd;
        _credentialManager.Setup(m => m.DecryptCredential(
                It.IsAny<string>(), It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>(),
                out outPwd))
            .Returns(CredentialLookupStatus.Success);

        _autoTyper.Setup(a => a.TypeToWindow(It.IsAny<IntPtr>(), It.IsAny<SecureString>()))
            .Returns(AutoTypeResult.Success);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
        _decryptedPwd.Dispose();
        _parent.Dispose();
    }

    private AccountPasswordHandler CreateHandler() =>
        new(_modalCoordinator.Object, _credentialManager.Object, _accountPassword.Object, _pinService.Object,
            _log.Object, new SidDisplayNameResolver(new Mock<ISidResolver>().Object),
            _autoTyper.Object, _secureDesktop.Object, _windowsHello.Object,
            new LambdaDatabaseProvider(() => new AppDatabase()),
            _evaluationLimitHelper.Object,
            new SecureClipboardService());

    [Fact]
    public async Task TypePassword_HelloVerified_UpdatesLastPinVerifiedAtAndDecryptsCredential()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Verified);

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, _session, _store, _guard, _parent,
            new IntPtr(1), _ => { });

        Assert.NotNull(_session.LastPinVerifiedAt);
        _credentialManager.Verify(m => m.DecryptCredential(
            accountRow.Sid, _store, _pinKey, out It.Ref<SecureString?>.IsAny), Times.Once);
        _autoTyper.Verify(a => a.TypeToWindow(new IntPtr(1), It.IsAny<SecureString>()), Times.Once);
    }

    [Theory]
    [InlineData(HelloVerificationResult.Canceled)]
    [InlineData(HelloVerificationResult.NotAvailable)]
    [InlineData(HelloVerificationResult.Failed)]
    public async Task TypePassword_HelloNotVerified_PromptsPinAndReturnsEarlyWhenPinNotEntered(HelloVerificationResult helloResult)
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(helloResult);

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, _session, _store, _guard, _parent,
            new IntPtr(1), _ => { });

        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        Assert.Null(_session.LastPinVerifiedAt);
        _credentialManager.Verify(m => m.DecryptCredential(
            It.IsAny<string>(), It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>(),
            out It.Ref<SecureString?>.IsAny), Times.Never);
    }

    [Fact]
    public async Task TypePassword_RecentlyVerified_SkipsHelloAndDecryptsCredential()
    {
        _session.LastPinVerifiedAt = DateTime.UtcNow;

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, _session, _store, _guard, _parent,
            new IntPtr(1), _ => { });

        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        _credentialManager.Verify(m => m.DecryptCredential(
            accountRow.Sid, _store, _pinKey, out It.Ref<SecureString?>.IsAny), Times.Once);
    }

    [Fact]
    public async Task TypePassword_NonHelloMode_SkipsHelloService()
    {
        _database.Settings.UnlockMode = UnlockMode.Pin;
        _session.LastPinVerifiedAt = DateTime.UtcNow;

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, _session, _store, _guard, _parent,
            new IntPtr(1), _ => { });

        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        _credentialManager.Verify(m => m.DecryptCredential(
            accountRow.Sid, _store, _pinKey, out It.Ref<SecureString?>.IsAny), Times.Once);
    }

    [Theory]
    [InlineData(CredentialLookupStatus.NotFound)]
    [InlineData(CredentialLookupStatus.MissingPassword)]
    public async Task TypePassword_NonSuccessCredentialStatus_DoesNotTypePasswordAndSetsStatus(CredentialLookupStatus status)
    {
        // Arrange: PIN already verified → EnsurePinVerifiedAsync bypassed.
        // DecryptCredential returns a non-success status (no stored password found).
        _session.LastPinVerifiedAt = DateTime.UtcNow;

        SecureString? nullPwd = null;
        _credentialManager.Setup(m => m.DecryptCredential(
                It.IsAny<string>(), It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>(),
                out nullPwd))
            .Returns(status);

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        string? capturedStatus = null;
        await handler.TypePasswordAsync(accountRow, _session, _store, _guard, _parent,
            new IntPtr(1), s => capturedStatus = s);

        // DecryptCredential was called but no typing should occur
        _credentialManager.Verify(m => m.DecryptCredential(
            accountRow.Sid, _store, _pinKey, out It.Ref<SecureString?>.IsAny), Times.Once);
        _autoTyper.Verify(a => a.TypeToWindow(It.IsAny<IntPtr>(), It.IsAny<SecureString>()), Times.Never);
        Assert.Equal("No stored password found.", capturedStatus);
    }
}
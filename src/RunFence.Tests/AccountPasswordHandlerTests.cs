using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountPasswordHandlerTests : IDisposable
{
    private readonly TestCredentialDecryptionService _credentialDecryption = new();
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IWindowsHelloService> _windowsHello = new();
    private readonly Mock<IPasswordAutoTyper> _autoTyper = new();
    private readonly Mock<ISecureDesktopRunner> _secureDesktop = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<ISecureClipboardService> _secureClipboard = new();

    private readonly AppDatabase _database = new();
    private readonly SessionContext _session;
    private readonly CredentialStore _store;
    private readonly OperationGuard _guard = new();
    private readonly Panel _parent = new();
    private readonly ProtectedString _decryptedPwd;
    private string? _capturedStatus;

    public AccountPasswordHandlerTests()
    {
        _store = new CredentialStore();
        _session = new SessionContext
        {
            Database = _database,
            CredentialStore = _store,
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;

        _sessionProvider.Setup(s => s.GetSession()).Returns(_session);

        _decryptedPwd = new ProtectedString();
        _decryptedPwd.AppendChar('p');
        _decryptedPwd.MakeReadOnly();

        _credentialDecryption.Handler = (string _, CredentialStore _, ReadOnlySpan<byte> _, out CredentialEntry? credEntry, out ProtectedString? password) =>
        {
            credEntry = null;
            password = _decryptedPwd;
            return CredentialLookupStatus.Success;
        };

        _autoTyper.Setup(a => a.TypeToWindow(It.IsAny<IntPtr>(), It.IsAny<ProtectedString>()))
            .Returns(AutoTypeResult.Success);
    }

    public void Dispose()
    {
        _session.Dispose();
        _decryptedPwd.Dispose();
        _parent.Dispose();
    }

    private AccountPasswordAccessHandler CreateHandler()
    {
        var handler = new AccountPasswordAccessHandler(
            _credentialDecryption,
            _log.Object,
            _pinService.Object,
            _secureDesktop.Object,
            _windowsHello.Object,
            _sessionProvider.Object,
            _secureClipboard.Object,
            _autoTyper.Object);
        handler.Initialize(_guard, _parent, s => _capturedStatus = s);
        return handler;
    }

    [Fact]
    public async Task TypePassword_HelloVerified_UpdatesLastPinVerifiedAtAndDecryptsCredential()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Verified);

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        Assert.Null(_capturedStatus);

        await handler.TypePasswordAsync(accountRow, new IntPtr(1));

        Assert.NotNull(_session.LastPinVerifiedAt);
        Assert.Equal("Password typed.", _capturedStatus);
        Assert.Single(_credentialDecryption.Calls);
        _autoTyper.Verify(a => a.TypeToWindow(new IntPtr(1), It.IsAny<ProtectedString>()), Times.Once);
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

        await handler.TypePasswordAsync(accountRow, new IntPtr(1));

        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        Assert.Null(_session.LastPinVerifiedAt);
        Assert.Empty(_credentialDecryption.Calls);
        _secureClipboard.Verify(s => s.ClearActiveSecretExposure(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task TypePassword_RecentlyVerified_SkipsHelloAndDecryptsCredential()
    {
        _session.LastPinVerifiedAt = DateTime.UtcNow;

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, new IntPtr(1));

        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        Assert.Single(_credentialDecryption.Calls);
        _autoTyper.Verify(a => a.TypeToWindow(new IntPtr(1), It.IsAny<ProtectedString>()), Times.Once);
        Assert.Equal("Password typed.", _capturedStatus);
    }

    [Fact]
    public async Task TypePassword_NonHelloMode_SkipsHelloService()
    {
        _database.Settings.UnlockMode = UnlockMode.Pin;
        _session.LastPinVerifiedAt = DateTime.UtcNow;

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, new IntPtr(1));

        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        Assert.Single(_credentialDecryption.Calls);
        _autoTyper.Verify(a => a.TypeToWindow(new IntPtr(1), It.IsAny<ProtectedString>()), Times.Once);
        Assert.Equal("Password typed.", _capturedStatus);
    }

    [Theory]
    [InlineData(CredentialLookupStatus.NotFound)]
    [InlineData(CredentialLookupStatus.MissingPassword)]
    public async Task TypePassword_NonSuccessCredentialStatus_DoesNotTypePasswordAndSetsStatus(CredentialLookupStatus status)
    {
        // Arrange: PIN already verified. EnsurePinVerifiedAsync bypassed.
        // DecryptCredential returns a non-success status (no stored password found).
        _session.LastPinVerifiedAt = DateTime.UtcNow;

        _credentialDecryption.Handler = (string _, CredentialStore _, ReadOnlySpan<byte> _, out CredentialEntry? credEntry, out ProtectedString? password) =>
        {
            credEntry = null;
            password = null;
            return status;
        };

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, new IntPtr(1));

        // TryDecryptCredential was called but no typing should occur
        Assert.Single(_credentialDecryption.Calls);
        _autoTyper.Verify(a => a.TypeToWindow(It.IsAny<IntPtr>(), It.IsAny<ProtectedString>()), Times.Never);
        Assert.Equal("No stored password found.", _capturedStatus);
        _secureClipboard.Verify(s => s.ClearActiveSecretExposure(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CopyPassword_PinNotVerified_ClearsActiveSecretExposure()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Canceled);

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.CopyPasswordAsync(accountRow);

        _secureClipboard.Verify(s => s.ClearActiveSecretExposure(), Times.AtLeastOnce);
        _secureClipboard.Verify(s => s.CopyProtectedStringToClipboard(It.IsAny<ProtectedString>()), Times.Never);
    }

    [Fact]
    public async Task CopyPassword_Success_SchedulesClearAndKeepsActiveExposureCleanupManaged()
    {
        _session.LastPinVerifiedAt = DateTime.UtcNow;
        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.CopyPasswordAsync(accountRow);

        _secureClipboard.Verify(s => s.CopyProtectedStringToClipboard(It.IsAny<ProtectedString>()), Times.Once);
        _secureClipboard.Verify(s => s.ScheduleClipboardClear(), Times.Once);
    }

    [Fact]
    public async Task TypePassword_FocusChanged_SetsStoppedStatus()
    {
        _session.LastPinVerifiedAt = DateTime.UtcNow;
        _autoTyper.Setup(a => a.TypeToWindow(It.IsAny<IntPtr>(), It.IsAny<ProtectedString>()))
            .Returns(AutoTypeResult.FocusChanged);

        var handler = CreateHandler();
        var accountRow = new AccountRow(null, "test", "S-1-5-21-1", false);

        await handler.TypePasswordAsync(accountRow, new IntPtr(1));

        Assert.Equal("Typing stopped: focus changed.", _capturedStatus);
    }

    private sealed class TestCredentialDecryptionService : ICredentialDecryptionService
    {
        public delegate CredentialLookupStatus TryDecryptHandler(
            string accountSid,
            CredentialStore credentialStore,
            ReadOnlySpan<byte> pinDerivedKey,
            out CredentialEntry? credEntry,
            out ProtectedString? password);

        public List<string> Calls { get; } = [];
        public TryDecryptHandler? Handler { get; set; }

        public RunFence.Launch.LaunchCredentials? DecryptAndResolve(
            string accountSid,
            CredentialStore credentialStore,
            ReadOnlySpan<byte> pinDerivedKey,
            IReadOnlyDictionary<string, string>? sidNames,
            out CredentialLookupStatus status)
            => throw new NotSupportedException();

        public CredentialLookupStatus TryDecryptCredential(
            string accountSid,
            CredentialStore credentialStore,
            ReadOnlySpan<byte> pinDerivedKey,
            out CredentialEntry? credEntry,
            out ProtectedString? password)
        {
            Calls.Add(accountSid);
            return Handler?.Invoke(accountSid, credentialStore, pinDerivedKey, out credEntry, out password)
                ?? throw new InvalidOperationException("Handler was not configured.");
        }

        public CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credentialStore)
            => throw new NotSupportedException();
    }
}

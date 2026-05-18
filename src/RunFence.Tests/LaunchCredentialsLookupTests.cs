using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class LaunchCredentialsLookupTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IByteArrayCredentialEncryptionService> _encryptionService = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly SecureSecret _protectedPinKey;
    private readonly LaunchCredentialsLookup _lookup;

    public LaunchCredentialsLookupTests()
    {
        _protectedPinKey = TestSecretFactory.FromBytes(_pinDerivedKey);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithOwnedPinDerivedKey(_protectedPinKey));

        var credentialDecryption = new CredentialDecryptionService(
            new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
            _sidResolver.Object,
            _interactiveUserSidResolver.Object);

        _lookup = new LaunchCredentialsLookup(
            sessionProvider.Object,
            credentialDecryption,
            () => new InlineUiThreadInvoker(action => action()));
    }

    public void Dispose() => _protectedPinKey.Dispose();

    [Fact]
    public void Lookup_KnownSidWithPassword_ReturnsCredentialsTokenSource()
    {
        // Arrange — credential with encrypted password → LaunchTokenSource.Credentials
        var encryptedBytes = new byte[] { 1, 2, 3 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = TestSid,
            EncryptedPassword = encryptedBytes
        });

        using var password = new ProtectedString();
        password.AppendChar('p');
        password.MakeReadOnly();
        _encryptionService.Setup(e => e.Decrypt(
                encryptedBytes,
                It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))))
            .Returns(password);

        _sidResolver.Setup(s => s.TryResolveName(TestSid)).Returns("DOMAIN\\testuser");

        // Act
        var result = _lookup.GetBySid(TestSid);

        // Assert
        Assert.Equal(LaunchTokenSource.Credentials, result.TokenSource);
        Assert.NotNull(result.Password);
    }

    [Fact]
    public void Lookup_CurrentAccountSid_ReturnsCurrentProcessTokenSource()
    {
        // Arrange — credential entry for the current account (SID = running process SID)
        // → no password needed; token source = CurrentProcess
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = currentSid
        });

        // Act
        var result = _lookup.GetBySid(currentSid);

        // Assert
        Assert.Equal(LaunchTokenSource.CurrentProcess, result.TokenSource);
        Assert.Null(result.Password);
    }

    [Fact]
    public void Lookup_UnknownSid_ThrowsCredentialNotFoundException()
    {
        // Arrange — empty credential store, SID not recognized

        // Act & Assert
        var ex = Assert.Throws<CredentialNotFoundException>(() => _lookup.GetBySid(TestSid));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lookup_KnownSidNoPassword_ThrowsMissingPasswordException()
    {
        // Arrange — credential entry exists but EncryptedPassword is empty
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = TestSid,
            EncryptedPassword = []
        });

        // Act & Assert
        Assert.Throws<MissingPasswordException>(() => _lookup.GetBySid(TestSid));
    }

    [Fact]
    public void Lookup_InteractiveUserWithStoredPassword_UsesInteractiveUserWithDecryptedPassword()
    {
        var interactiveSid = "S-1-5-21-1234567890-1234567890-1234567890-8888";
        var encryptedBytes = new byte[] { 4, 5, 6 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = interactiveSid,
            EncryptedPassword = encryptedBytes
        });

        using var password = new ProtectedString();
        password.AppendChar('p');
        password.MakeReadOnly();
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);
        _encryptionService.Setup(e => e.Decrypt(
                encryptedBytes,
                It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))))
            .Returns(password);
        _sidResolver.Setup(s => s.TryResolveName(interactiveSid)).Returns(@"DOMAIN\interuser");

        var result = _lookup.GetBySid(interactiveSid);

        Assert.Equal(LaunchTokenSource.InteractiveUser, result.TokenSource);
        Assert.NotNull(result.Password);
        Assert.Same(password, result.Password);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lookup_SystemSid_ReturnsSystemAccountTokenSource(bool hasCredentialEntry)
    {
        // Arrange — SYSTEM always returns SystemAccount, even if a spurious credential entry exists.
        if (hasCredentialEntry)
            _credentialStore.Credentials.Add(new CredentialEntry
            {
                Id = Guid.NewGuid(),
                Sid = Core.SidConstants.SystemSid,
                EncryptedPassword = [1, 2, 3]
            });

        // Act
        var result = _lookup.GetBySid(Core.SidConstants.SystemSid);

        // Assert
        Assert.Equal(LaunchTokenSource.SystemAccount, result.TokenSource);
        Assert.Null(result.Password);
    }

    [Fact]
    public async Task Lookup_WorkerThread_CapturesSessionStateOnUiThread_AndDecryptsOnUiThread()
    {
        var encryptedBytes = new byte[] { 7, 8, 9 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = TestSid,
            EncryptedPassword = encryptedBytes
        });

        using var password = new ProtectedString();
        password.AppendChar('q');
        password.MakeReadOnly();

        using var uiInvoker = new DedicatedThreadUiInvoker();
        var credentialDecryption = new CredentialDecryptionService(
            new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
            _sidResolver.Object,
            _interactiveUserSidResolver.Object);
        var sessionProvider = new Mock<ISessionProvider>();
        var session = new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithOwnedPinDerivedKey(_protectedPinKey);
        var sessionThreadId = 0;
        sessionProvider.Setup(s => s.GetSession())
            .Callback(() => sessionThreadId = Environment.CurrentManagedThreadId)
            .Returns(session);

        var lookup = new LaunchCredentialsLookup(
            sessionProvider.Object,
            credentialDecryption,
            () => uiInvoker);

        var lookupThreadId = 0;
        int decryptThreadId = 0;
        _encryptionService.Setup(e => e.Decrypt(
                encryptedBytes,
                It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))))
            .Callback(() => decryptThreadId = Environment.CurrentManagedThreadId)
            .Returns(password);
        _sidResolver.Setup(s => s.TryResolveName(TestSid)).Returns("DOMAIN\\testuser");

        var result = await Task.Run(() =>
        {
            lookupThreadId = Environment.CurrentManagedThreadId;
            return lookup.GetBySid(TestSid);
        });

        Assert.Equal(LaunchTokenSource.Credentials, result.TokenSource);
        Assert.Equal(uiInvoker.ThreadId, sessionThreadId);
        Assert.NotEqual(uiInvoker.ThreadId, lookupThreadId);
        Assert.Equal(uiInvoker.ThreadId, decryptThreadId);
    }

    [Fact]
    public void Lookup_CredentialStoreMutatedAfterUiCapture_UsesCapturedSnapshot()
    {
        var originalBytes = new byte[] { 1, 2, 3 };
        var mutatedBytes = new byte[] { 9, 9, 9 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = TestSid,
            EncryptedPassword = originalBytes
        });

        using var originalPassword = new ProtectedString();
        originalPassword.AppendChar('o');
        originalPassword.MakeReadOnly();

        using var mutatedPassword = new ProtectedString();
        mutatedPassword.AppendChar('m');
        mutatedPassword.MakeReadOnly();

        _encryptionService.Setup(e => e.Decrypt(
                It.Is<byte[]>(bytes => bytes.SequenceEqual(originalBytes)),
                It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))))
            .Returns(originalPassword);
        _encryptionService.Setup(e => e.Decrypt(
                It.Is<byte[]>(bytes => bytes.SequenceEqual(mutatedBytes)),
                It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))))
            .Returns(mutatedPassword);
        _sidResolver.Setup(s => s.TryResolveName(TestSid)).Returns("DOMAIN\\testuser");

        var lookup = new LaunchCredentialsLookup(
            new LambdaSessionProvider(() => new SessionContext
{
                Database = _database,
                CredentialStore = _credentialStore,
            }.WithOwnedPinDerivedKey(_protectedPinKey)),
            new CredentialDecryptionService(
                new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
                _sidResolver.Object,
                _interactiveUserSidResolver.Object),
            () => new InlineUiThreadInvoker(action =>
            {
                action();
                _credentialStore.Credentials[0].EncryptedPassword = mutatedBytes;
            }));

        var result = lookup.GetBySid(TestSid);

        Assert.Same(originalPassword, result.Password);
        _encryptionService.Verify(e => e.Decrypt(
            It.Is<byte[]>(bytes => bytes.SequenceEqual(originalBytes)),
            It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))), Times.Once);
        _encryptionService.Verify(e => e.Decrypt(
            It.Is<byte[]>(bytes => bytes.SequenceEqual(mutatedBytes)),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Lookup_PinKeyReplacedAfterUiInvocation_ReturnsCompletedLookup()
    {
        var encryptedBytes = new byte[] { 3, 4, 5 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = TestSid,
            EncryptedPassword = encryptedBytes
        });

        using var password = new ProtectedString();
        password.AppendChar('r');
        password.MakeReadOnly();

        var replacementKeyBytes = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        using var session = new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithOwnedPinDerivedKey(_protectedPinKey);

        _encryptionService.Setup(e => e.Decrypt(
                encryptedBytes,
                It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))))
            .Returns(password);
        _sidResolver.Setup(s => s.TryResolveName(TestSid)).Returns("DOMAIN\\testuser");

        var lookup = new LaunchCredentialsLookup(
            new LambdaSessionProvider(() => session),
            new CredentialDecryptionService(
                new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
                _sidResolver.Object,
                _interactiveUserSidResolver.Object),
            () => new ReplacePinKeyAfterInvokeUiThreadInvoker(
                session,
                TestSecretFactory.FromBytes(replacementKeyBytes)));

        var result = lookup.GetBySid(TestSid);

        Assert.Equal(LaunchTokenSource.Credentials, result.TokenSource);
        Assert.Same(password, result.Password);
        _encryptionService.Verify(e => e.Decrypt(
            encryptedBytes,
            It.Is<byte[]>(key => key.SequenceEqual(_pinDerivedKey))), Times.Once);
    }

    private sealed class ReplacePinKeyAfterInvokeUiThreadInvoker(
        SessionContext session,
        SecureSecret replacementKey) : IUiThreadInvoker
    {
        public T Invoke<T>(Func<T> func)
        {
            T result = func();
            session.ReplacePinDerivedKey(replacementKey);
            return result;
        }

        public void BeginInvoke(Action action) => action();
    }
}

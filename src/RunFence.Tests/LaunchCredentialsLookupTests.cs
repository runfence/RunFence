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

    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly ProtectedBuffer _protectedPinKey;
    private readonly LaunchCredentialsLookup _lookup;

    public LaunchCredentialsLookupTests()
    {
        _protectedPinKey = new ProtectedBuffer(_pinDerivedKey, protect: false);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
            PinDerivedKey = _protectedPinKey
        });

        var credentialDecryption = new CredentialDecryptionService(
            _encryptionService.Object, _sidResolver.Object);

        _lookup = new LaunchCredentialsLookup(
            sessionProvider.Object,
            credentialDecryption);
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
        _encryptionService.Setup(e => e.Decrypt(encryptedBytes, _pinDerivedKey)).Returns(password);

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
}

using System.Security;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class CredentialHelperTests
{
    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly CredentialDecryptionService _service;

    public CredentialHelperTests()
    {
        _service = new CredentialDecryptionService(_encryptionService.Object, _sidResolver.Object);
    }

    [Fact]
    public void TryDecryptCredential_NotFound_ReturnsNotFound()
    {
        var store = new CredentialStore();
        var accountSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        var status = _service.TryDecryptCredential(
            accountSid, store, _pinDerivedKey, out var credEntry, out var password);

        Assert.Equal(CredentialLookupStatus.NotFound, status);
        Assert.Null(credEntry);
        Assert.Null(password);
    }

    [Fact]
    public void TryDecryptCredential_CurrentAccount_ReturnsCurrentAccountWithNullPassword()
    {
        var accountSid = SidResolutionHelper.GetCurrentUserSid();
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = accountSid }]
        };

        var status = _service.TryDecryptCredential(
            accountSid, store, _pinDerivedKey, out var credEntry, out var password);

        Assert.Equal(CredentialLookupStatus.CurrentAccount, status);
        Assert.NotNull(credEntry);
        Assert.True(credEntry.IsCurrentAccount);
        Assert.Null(password);
    }

    [Fact]
    public void TryDecryptCredential_SidNotInStore_ReturnsNotFound()
    {
        // Store has an entry for a DIFFERENT SID — the requested SID is absent
        var requestedSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var otherSid = "S-1-5-21-9999999999-9999999999-9999999999-1002";
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = otherSid, EncryptedPassword = [] }]
        };

        var status = _service.TryDecryptCredential(
            requestedSid, store, _pinDerivedKey, out var credEntry, out var password);

        Assert.Equal(CredentialLookupStatus.NotFound, status);
        Assert.Null(credEntry);
        Assert.Null(password);
    }

    [Fact]
    public void TryDecryptCredential_EmptyEncryptedPassword_ReturnsMissingPassword()
    {
        var accountSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = accountSid, EncryptedPassword = [] }]
        };

        var status = _service.TryDecryptCredential(
            accountSid, store, _pinDerivedKey, out _, out var password);

        Assert.Equal(CredentialLookupStatus.MissingPassword, status);
        Assert.Null(password);
    }

    [Fact]
    public void TryDecryptCredential_Success_DecryptsAndReturnsPassword()
    {
        var accountSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var encryptedBytes = new byte[] { 1, 2, 3 };
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = accountSid, EncryptedPassword = encryptedBytes }]
        };

        var expectedPassword = new SecureString();
        expectedPassword.AppendChar('p');
        expectedPassword.MakeReadOnly();

        _encryptionService
            .Setup(e => e.Decrypt(encryptedBytes, _pinDerivedKey))
            .Returns(expectedPassword);

        var status = _service.TryDecryptCredential(
            accountSid, store, _pinDerivedKey, out var credEntry, out var password);

        Assert.Equal(CredentialLookupStatus.Success, status);
        Assert.NotNull(credEntry);
        Assert.Equal(accountSid, credEntry.Sid);
        Assert.Same(expectedPassword, password);
    }

    // --- DecryptAndResolve tests ---

    [Fact]
    public void DecryptAndResolve_NotFound_ReturnsNull()
    {
        var store = new CredentialStore();
        var accountSid = "S-1-5-21-1234567890-1234567890-1234567890-9999";

        var result = _service.DecryptAndResolve(
            accountSid, store, _pinDerivedKey, null, out var status);

        Assert.Null(result);
        Assert.Equal(CredentialLookupStatus.NotFound, status);
    }

    [Fact]
    public void DecryptAndResolve_MissingPassword_ReturnsNull()
    {
        var accountSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = accountSid, EncryptedPassword = [] }]
        };

        var result = _service.DecryptAndResolve(
            accountSid, store, _pinDerivedKey, null, out var status);

        Assert.Null(result);
        Assert.Equal(CredentialLookupStatus.MissingPassword, status);
    }

    [Fact]
    public void DecryptAndResolve_CurrentAccount_ReturnsCurrentProcessTokenSource()
    {
        var accountSid = SidResolutionHelper.GetCurrentUserSid();
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = accountSid }]
        };

        var result = _service.DecryptAndResolve(
            accountSid, store, _pinDerivedKey, null, out var status);

        Assert.NotNull(result);
        Assert.Equal(LaunchTokenSource.CurrentProcess, result.Value.TokenSource);
        Assert.Equal(CredentialLookupStatus.CurrentAccount, status);
        Assert.Null(result.Value.Password);
    }

    [Fact]
    public void DecryptAndResolve_Success_ReturnsCredentialsTokenSourceWithPassword()
    {
        var accountSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var encryptedBytes = new byte[] { 1, 2, 3 };
        var store = new CredentialStore
        {
            Credentials = [new() { Id = Guid.NewGuid(), Sid = accountSid, EncryptedPassword = encryptedBytes }]
        };
        var expectedPassword = new SecureString();
        expectedPassword.AppendChar('p');
        expectedPassword.MakeReadOnly();
        _encryptionService.Setup(e => e.Decrypt(encryptedBytes, _pinDerivedKey)).Returns(expectedPassword);
        _sidResolver.Setup(r => r.TryResolveName(accountSid)).Returns(@"DOMAIN\user1");

        var result = _service.DecryptAndResolve(
            accountSid, store, _pinDerivedKey, null, out var status);

        Assert.NotNull(result);
        Assert.Equal(LaunchTokenSource.Credentials, result.Value.TokenSource);
        Assert.Equal(CredentialLookupStatus.Success, status);
        Assert.Same(expectedPassword, result.Value.Password);
        Assert.Equal("DOMAIN", result.Value.Domain);
        Assert.Equal("user1", result.Value.Username);
    }

    // --- InteractiveUser status ---
    //
    // Both code paths that return InteractiveUser rely on SidResolutionHelper.GetInteractiveUserSid(),
    // which is a static value initialized only during RunFence startup (null in test environments).
    // The two paths differ in whether credEntry is null:
    //   (a) No stored credential + SID == GetInteractiveUserSid() → InteractiveUser, credEntry = null
    //   (b) Stored credential + credEntry.IsInteractiveUser == true → InteractiveUser, credEntry != null
    // Callers must handle null credEntry for InteractiveUser status (documented in CLAUDE.md).
    //
    // Since GetInteractiveUserSid() returns null in tests, neither branch is directly reachable.
    // The tests below verify the fallthrough behavior: when the interactive SID is unset,
    // the code must not accidentally return InteractiveUser for unrelated SIDs.

    [Fact]
    public void TryDecryptCredential_NoStoredCredential_WhenInteractiveSidUninitialized_ReturnsNotFound()
    {
        // In a test process GetInteractiveUserSid() returns null → the InteractiveUser guard is
        // skipped → any SID not in the store returns NotFound (not InteractiveUser).
        var nonCurrentSid = "S-1-5-21-1234567890-1234567890-1234567890-9999";
        var store = new CredentialStore();

        var status = _service.TryDecryptCredential(
            nonCurrentSid, store, _pinDerivedKey, out var credEntry, out var password);

        Assert.Equal(CredentialLookupStatus.NotFound, status);
        Assert.Null(credEntry);
        Assert.Null(password);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }
}

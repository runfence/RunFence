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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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

        var expectedPassword = new ProtectedString();
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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
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
        var expectedPassword = new ProtectedString();
        expectedPassword.AppendChar('p');
        expectedPassword.MakeReadOnly();
        _encryptionService.Setup(e => e.Decrypt(encryptedBytes, _pinDerivedKey)).Returns(expectedPassword);
        // Use a domain name with characters that cannot appear in a Windows machine name (hyphens
        // are valid, but this multi-part form is too long to be a real NetBIOS name)
        _sidResolver.Setup(r => r.TryResolveName(accountSid)).Returns(@"RFTEST-AD-DOMAIN\user1");

        var result = _service.DecryptAndResolve(
            accountSid, store, _pinDerivedKey, null, out var status);

        Assert.NotNull(result);
        Assert.Equal(LaunchTokenSource.Credentials, result.Value.TokenSource);
        Assert.Equal(CredentialLookupStatus.Success, status);
        Assert.Same(expectedPassword, result.Value.Password);
        Assert.Equal("RFTEST-AD-DOMAIN", result.Value.Domain);
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

    // --- CheckCredential: status per condition without calling Decrypt ---
    //
    // CheckCredential is the same pre-decrypt resolution path as TryDecryptCredential but without
    // decryption. Verifying that Decrypt is never called confirms the "check only" contract.

    [Fact]
    public void CheckCredential_NoEntry_ReturnsNotFound()
    {
        var store = new CredentialStore();

        var status = _service.CheckCredential("S-1-5-21-9999999999-9999999999-9999999999-1001", store);

        Assert.Equal(CredentialLookupStatus.NotFound, status);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void CheckCredential_CurrentAccount_ReturnsCurrentAccountWithoutDecrypting()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = currentSid }]
        };

        var status = _service.CheckCredential(currentSid, store);

        Assert.Equal(CredentialLookupStatus.CurrentAccount, status);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void CheckCredential_EmptyEncryptedPassword_ReturnsMissingPasswordWithoutDecrypting()
    {
        var accountSid = "S-1-5-21-9999999999-9999999999-9999999999-1001";
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = accountSid, EncryptedPassword = [] }]
        };

        var status = _service.CheckCredential(accountSid, store);

        Assert.Equal(CredentialLookupStatus.MissingPassword, status);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void CheckCredential_WithEncryptedPassword_ReturnsSuccessWithoutDecrypting()
    {
        // CheckCredential must return Success for a stored credential with an encrypted password
        // without calling Decrypt — status is determined by the pre-decrypt check only.
        var accountSid = "S-1-5-21-9999999999-9999999999-9999999999-1001";
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = accountSid, EncryptedPassword = [1, 2, 3] }]
        };

        var status = _service.CheckCredential(accountSid, store);

        Assert.Equal(CredentialLookupStatus.Success, status);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Theory]
    [InlineData(0, CredentialLookupStatus.MissingPassword)]
    [InlineData(3, CredentialLookupStatus.Success)]
    public void TryDecryptCredential_NonSpecialSid_StatusMatchesEncryptedPasswordPresence(
        int encryptedPasswordLength, CredentialLookupStatus expectedStatus)
    {
        // For a stored credential that is neither CurrentAccount nor InteractiveUser,
        // the status solely depends on whether EncryptedPassword has content.
        var accountSid = "S-1-5-21-9999999999-9999999999-9999999999-1001";
        var encrypted = new byte[encryptedPasswordLength];
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = accountSid, EncryptedPassword = encrypted }]
        };
        if (encryptedPasswordLength > 0)
            _encryptionService.Setup(e => e.Decrypt(encrypted, _pinDerivedKey)).Returns(new ProtectedString());

        var status = _service.TryDecryptCredential(accountSid, store, _pinDerivedKey, out _, out _);

        Assert.Equal(expectedStatus, status);
    }

    // --- SYSTEM account tests ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryDecryptCredential_SystemSid_ReturnsSystemAccount(bool hasCredentialEntry)
    {
        // SYSTEM check is first in ResolvePreDecryptStatus — no credential entry is ever needed.
        // This holds even when a CredentialEntry for SYSTEM accidentally exists in the store.
        var store = new CredentialStore();
        if (hasCredentialEntry)
            store.Credentials.Add(new CredentialEntry { Id = Guid.NewGuid(), Sid = Core.SidConstants.SystemSid, EncryptedPassword = [1, 2, 3] });

        var status = _service.TryDecryptCredential(
            Core.SidConstants.SystemSid, store, _pinDerivedKey, out var credEntry, out var password);

        Assert.Equal(CredentialLookupStatus.SystemAccount, status);
        Assert.Null(credEntry);
        Assert.Null(password);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void DecryptAndResolve_SystemSid_ReturnsSystemAccountCredentials()
    {
        // SYSTEM SID → SystemAccount status → LaunchTokenSource.SystemAccount; no password.
        var store = new CredentialStore();
        _sidResolver.Setup(r => r.TryResolveName(Core.SidConstants.SystemSid)).Returns(@"NT AUTHORITY\SYSTEM");

        var result = _service.DecryptAndResolve(
            Core.SidConstants.SystemSid, store, _pinDerivedKey, null, out var status);

        Assert.NotNull(result);
        Assert.Equal(CredentialLookupStatus.SystemAccount, status);
        Assert.Equal(LaunchTokenSource.SystemAccount, result.Value.TokenSource);
        Assert.Null(result.Value.Password);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void CheckCredential_SystemSid_ReturnsSystemAccount()
    {
        // SYSTEM check is first — no credential entry needed.
        var store = new CredentialStore();

        var status = _service.CheckCredential(Core.SidConstants.SystemSid, store);

        Assert.Equal(CredentialLookupStatus.SystemAccount, status);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }
}

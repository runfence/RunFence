using System.Runtime.InteropServices;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountCredentialManagerTests : IDisposable
{
    private const string FakeSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";
    private const string FakeSid2 = "S-1-5-21-9999999999-9999999999-9999999999-9002";

    private readonly AccountCredentialManager _manager;
    private readonly ICredentialDecryptionService _credentialDecryption;
    private readonly ProtectedBuffer _pinKey;

    public AccountCredentialManagerTests()
    {
        var pinKeyBytes = new byte[32];
        new Random(42).NextBytes(pinKeyBytes);
        _pinKey = new ProtectedBuffer(pinKeyBytes, protect: false);

        var encryptionService = new CredentialEncryptionService();
        var sidResolver = new Mock<ISidResolver>();
        _credentialDecryption = new CredentialDecryptionService(encryptionService, sidResolver.Object);
        _manager = new AccountCredentialManager(encryptionService);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
    }

    // --- StoreCreatedUserCredential ---

    [Fact]
    public void StoreCreatedUserCredential_NewSid_AddsCredentialAndReturnsId()
    {
        // Arrange
        var store = new CredentialStore();
        using var password = new ProtectedString();
        foreach (var c in "pass")
            password.AppendChar(c);

        // Act
        var id = _manager.StoreCreatedUserCredential(FakeSid, password, store, _pinKey);

        // Assert
        Assert.NotNull(id);
        Assert.Single(store.Credentials);
        Assert.Equal(FakeSid, store.Credentials[0].Sid);
        Assert.Equal(id.Value, store.Credentials[0].Id);
        Assert.NotEmpty(store.Credentials[0].EncryptedPassword);
    }

    [Fact]
    public void StoreCreatedUserCredential_DuplicateSid_ReturnsNullAndDoesNotAdd()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });
        using var password = new ProtectedString();

        // Act
        var id = _manager.StoreCreatedUserCredential(FakeSid, password, store, _pinKey);

        // Assert
        Assert.Null(id);
        Assert.Single(store.Credentials); // no duplicate added
    }

    // --- AddNewCredential ---

    [Fact]
    public void AddNewCredential_NewSid_AddsCredentialAndReturnsSuccess()
    {
        // Arrange
        var store = new CredentialStore();
        using var password = new ProtectedString();
        foreach (var c in "pass")
            password.AppendChar(c);

        // Act
        var (success, id, error) = _manager.AddNewCredential(FakeSid, password, store, _pinKey);

        // Assert
        Assert.True(success);
        Assert.NotNull(id);
        Assert.Null(error);
        Assert.Single(store.Credentials);
        Assert.Equal(FakeSid, store.Credentials[0].Sid);
    }

    [Fact]
    public void AddNewCredential_DuplicateSid_ReturnsErrorAndDoesNotAdd()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });

        // Act
        var (success, id, error) = _manager.AddNewCredential(FakeSid, null, store, _pinKey);

        // Assert
        Assert.False(success);
        Assert.Null(id);
        Assert.NotNull(error);
        Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(store.Credentials); // no duplicate added
    }

    [Fact]
    public void AddNewCredential_NullPassword_AddsWithEmptyEncryptedPassword()
    {
        // Arrange
        var store = new CredentialStore();

        // Act
        var (success, _, _) = _manager.AddNewCredential(FakeSid, null, store, _pinKey);

        // Assert
        Assert.True(success);
        Assert.Single(store.Credentials);
        Assert.Empty(store.Credentials[0].EncryptedPassword);
    }

    // --- RemoveCredential ---

    [Fact]
    public void RemoveCredential_ExistingId_RemovesEntry()
    {
        // Arrange
        var store = new CredentialStore();
        var credId = Guid.NewGuid();
        store.Credentials.Add(new CredentialEntry { Id = credId, Sid = FakeSid });

        // Act
        _manager.RemoveCredential(credId, store);

        // Assert
        Assert.Empty(store.Credentials);
    }

    [Fact]
    public void RemoveCredential_WrongId_LeavesOtherCredentials()
    {
        // Arrange
        var store = new CredentialStore();
        var credId = Guid.NewGuid();
        store.Credentials.Add(new CredentialEntry { Id = credId, Sid = FakeSid });

        // Act
        _manager.RemoveCredential(Guid.NewGuid(), store);

        // Assert
        Assert.Single(store.Credentials);
    }

    // --- RemoveCredentialsBySid ---

    [Fact]
    public void RemoveCredentialsBySid_MatchingSid_RemovesAllMatching()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid2 }); // different SID

        // Act
        _manager.RemoveCredentialsBySid(FakeSid, store);

        // Assert
        Assert.Single(store.Credentials);
        Assert.Equal(FakeSid2, store.Credentials[0].Sid);
    }

    [Fact]
    public void RemoveCredentialsBySid_CaseInsensitive_RemovesEntry()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid.ToUpperInvariant() });

        // Act
        _manager.RemoveCredentialsBySid(FakeSid.ToLowerInvariant(), store);

        // Assert
        Assert.Empty(store.Credentials);
    }

    [Fact]
    public void RemoveCredentialsBySid_NoMatch_LeavesCredentialsUnchanged()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });

        // Act
        _manager.RemoveCredentialsBySid(FakeSid2, store);

        // Assert
        Assert.Single(store.Credentials);
    }

    // --- UpdateCredentialPassword ---

    [Fact]
    public void UpdateCredentialPassword_ReplacesEncryptedPassword()
    {
        // Arrange
        var originalPassword = new byte[] { 0x11, 0x22, 0x33 };
        var credEntry = new CredentialEntry { Sid = FakeSid, EncryptedPassword = originalPassword };

        using var newPassword = new ProtectedString();
        foreach (var c in "newpassword")
            newPassword.AppendChar(c);

        // Act
        _manager.UpdateCredentialPassword(credEntry, newPassword, _pinKey);

        // Assert — encrypted bytes are updated and differ from the original
        Assert.NotEqual(originalPassword, credEntry.EncryptedPassword);
        Assert.NotEmpty(credEntry.EncryptedPassword);
    }

    [Fact]
    public void UpdateCredentialPassword_EmptyPassword_ProducesNonEmptyEncryptedBytes()
    {
        // Arrange — even an empty password produces a non-empty ciphertext (AEAD tag + nonce)
        var credEntry = new CredentialEntry { Sid = FakeSid, EncryptedPassword = [] };
        using var emptyPassword = new ProtectedString();

        // Act
        _manager.UpdateCredentialPassword(credEntry, emptyPassword, _pinKey);

        // Assert
        Assert.NotEmpty(credEntry.EncryptedPassword);
    }

    // Note: the InteractiveUser credential path (TryDecryptCredential returning InteractiveUser
    // with null credEntry) cannot be tested under the non-admin test runner because resolving
    // the interactive user SID requires an active desktop session (explorer.exe running as
    // a different account). This path is covered by manual/integration testing only.

    // --- TryDecryptStoredPassword ---

    [Theory]
    [InlineData(false)] // no credential entry at all
    [InlineData(true)]  // credential exists but has empty EncryptedPassword
    public void TryDecryptStoredPassword_WhenNoStoredPassword_ReturnsFalseAndNull(bool addEmptyEntry)
    {
        // Arrange
        var store = new CredentialStore();
        if (addEmptyEntry)
            store.Credentials.Add(new CredentialEntry { Sid = FakeSid, EncryptedPassword = [] });

        // Act
        var result = _manager.TryDecryptStoredPassword(FakeSid, store, _pinKey, out var password);

        // Assert
        Assert.False(result);
        Assert.Null(password);
    }

    [Fact]
    public void TryDecryptStoredPassword_WhenPasswordIsStored_ReturnsTrueAndDecryptsCorrectly()
    {
        // Arrange
        var store = new CredentialStore();
        using var original = new ProtectedString();
        foreach (var c in "StrongPass!") original.AppendChar(c);
        original.MakeReadOnly();
        _manager.StoreCreatedUserCredential(FakeSid, original, store, _pinKey);

        // Act
        var result = _manager.TryDecryptStoredPassword(FakeSid, store, _pinKey, out var password);

        // Assert
        Assert.True(result);
        using var _ = password;
        Assert.NotNull(password);
        Assert.Equal("StrongPass!", ProtectedStringToString(password!));
    }

    [Fact]
    public void TryDecryptStoredPassword_ForCurrentAccountSid_DecryptsWhereTryDecryptCredentialWouldNot()
    {
        // The whole point of TryDecryptStoredPassword: TryDecryptCredential short-circuits for the
        // current-account SID (returns CurrentAccount status, null password), while
        // TryDecryptStoredPassword returns the actual stored password regardless of account type.
        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        // Arrange
        var store = new CredentialStore();
        using var original = new ProtectedString();
        foreach (var c in "AdminPass1") original.AppendChar(c);
        original.MakeReadOnly();
        _manager.StoreCreatedUserCredential(currentSid, original, store, _pinKey);

        // Act: run TryDecryptCredential in its own scope so the buffer is re-protected before
        // TryDecryptStoredPassword opens another scope (nested Unprotect is not supported).
        CredentialLookupStatus regularStatus;
        ProtectedString? regularPassword;
        {
            using var scope = _pinKey.Unprotect();
            regularStatus = _credentialDecryption.TryDecryptCredential(currentSid, store, scope.Data, out _, out regularPassword);
        }
        var directResult = _manager.TryDecryptStoredPassword(currentSid, store, _pinKey, out var directPassword);

        // Assert: TryDecryptCredential short-circuits, TryDecryptStoredPassword does not
        Assert.Equal(CredentialLookupStatus.CurrentAccount, regularStatus);
        Assert.Null(regularPassword);
        Assert.True(directResult);
        using var directPasswordDisposable = directPassword;
        Assert.NotNull(directPasswordDisposable);
        Assert.Equal("AdminPass1", ProtectedStringToString(directPasswordDisposable!));
    }

    [Fact]
    public void TryDecryptStoredPassword_WhenCiphertextIsInvalid_ReturnsFalseAndNull()
    {
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry
        {
            Sid = FakeSid,
            EncryptedPassword = [0x01, 0x02, 0x03]
        });

        var result = _manager.TryDecryptStoredPassword(FakeSid, store, _pinKey, out var password);

        Assert.False(result);
        Assert.Null(password);
    }

    private static string ProtectedStringToString(ProtectedString ss)
    {
        var bstr = ss.ToBSTR();
        try { return Marshal.PtrToStringBSTR(bstr)!; }
        finally { Marshal.ZeroFreeBSTR(bstr); }
    }
}

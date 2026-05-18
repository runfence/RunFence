using System.ComponentModel;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class AccountEditHelperTests : IDisposable
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";

    private readonly Mock<IModalCoordinator> _modalCoordinator = new();
    private readonly TestCredentialDecryptionService _credentialDecryption = new();
    private readonly Mock<IAccountPasswordService> _accountPassword = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<IConfigRepository> _configRepository = new();
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly SecureSecret _pinKey;

    private readonly AccountEditHelper _helper;

    private record TestAccountEditResult(ProtectedString? NewPassword, string? SettingsImportPath) : IAccountEditResult
    {
        public List<string> Errors { get; } = [];
    }

    public AccountEditHelperTests()
    {
        var persistenceHelper = new SessionPersistenceHelper(
            new Mock<ICredentialRepository>().Object,
            _configRepository.Object,
            new Mock<ISidNameCacheService>().Object,
            () => new InlineUiThreadInvoker(action => action()),
            new Mock<ILoggingService>().Object);

        var firewallApplyHelper = new FirewallApplyHelper(
            new Mock<IAccountFirewallSettingsApplier>().Object,
            new DynamicPortRangeChecker(new Mock<ILoggingService>().Object, new Mock<IUserConfirmationService>().Object, new StandardNetshCommandRunner()),
            new Mock<ILoggingService>().Object);

        _pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_pinKey);
        _sessionProvider.Setup(s => s.GetSession()).Returns(session);

        _helper = new AccountEditHelper(
            _modalCoordinator.Object,
            persistenceHelper,
            _credentialDecryption,
            _accountPassword.Object,
            _settingsTransferService.Object,
            _sessionProvider.Object,
            new OperationGuard(),
            firewallApplyHelper);
    }

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void ApplyPasswordChange_StoredPassword_FailedResult_FallsThrough()
    {
        // Arrange
        var newPwd = ProtectedString.FromChars("newpassword".AsSpan());
        var editResult = new TestAccountEditResult(newPwd, null);

        var oldPwd = new ProtectedString();
        oldPwd.AppendChar('x');
        _credentialDecryption.Handler = (string _, CredentialStore _, ReadOnlySpan<byte> _, out CredentialEntry? credEntry, out ProtectedString? password) =>
        {
            credEntry = null;
            password = oldPwd;
            return CredentialLookupStatus.Success;
        };

        _accountPassword
            .Setup(p => p.ChangeAccountPassword(TestSid, oldPwd, It.IsAny<ProtectedString>()))
            .Returns(new AccountPasswordResult(AccountPasswordStatus.Failed, TestSid, "Test non-Win32 error"));

        var credential = new CredentialEntry { Sid = TestSid, EncryptedPassword = [1, 2, 3] };
        var accountRow = new AccountRow(credential, "testuser", TestSid, hasStoredPassword: true);

        var result = _helper.ApplyPasswordChange(accountRow, editResult, isCurrentAccount: false);
        Assert.False(result);
        _modalCoordinator.Verify(m => m.RunOnSecureDesktop(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void ApplyPasswordChange_StoredPassword_InvalidPasswordResult_FallsThrough()
    {
        // Arrange
        var newPwd = ProtectedString.FromChars("newpassword".AsSpan());
        var editResult = new TestAccountEditResult(newPwd, null);

        var oldPwd = new ProtectedString();
        oldPwd.AppendChar('x');
        _credentialDecryption.Handler = (string _, CredentialStore _, ReadOnlySpan<byte> _, out CredentialEntry? credEntry, out ProtectedString? password) =>
        {
            credEntry = null;
            password = oldPwd;
            return CredentialLookupStatus.Success;
        };

        _accountPassword
            .Setup(p => p.ChangeAccountPassword(TestSid, oldPwd, It.IsAny<ProtectedString>()))
            .Returns(new AccountPasswordResult(AccountPasswordStatus.InvalidPassword, TestSid, "The specified network password is not correct."));

        // Modal coordinator does NOT execute the callback (which would show a dialog).
        // The default mock behavior returns without calling the action, so methodResult
        // stays DialogResult.None and the method returns false.

        var credential = new CredentialEntry { Sid = TestSid, EncryptedPassword = [1, 2, 3] };
        var accountRow = new AccountRow(credential, "testuser", TestSid, hasStoredPassword: true);

        // Act: invalid-password result falls through to method dialog.
        // Since RunOnSecureDesktop is mocked (no-op), methodResult remains None → returns false.
        var result = _helper.ApplyPasswordChange(accountRow, editResult, isCurrentAccount: false);

        // Assert
        Assert.False(result);
        _modalCoordinator.Verify(m => m.RunOnSecureDesktop(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task ImportDesktopSettingsAsync_DatabaseModified_DoesNotSaveConfigAgain()
    {
        var editResult = new TestAccountEditResult(null, @"C:\settings.json");
        var accountRow = new AccountRow(null, "testuser", TestSid, hasStoredPassword: false);
        _settingsTransferService
            .Setup(s => s.Import(editResult.SettingsImportPath!, TestSid, It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(true, "", DatabaseModified: true));

        using var owner = new Control();
        await _helper.ImportDesktopSettingsAsync(accountRow, editResult, owner);

        _configRepository.Verify(r => r.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
        Assert.Empty(editResult.Errors);
    }

    private sealed class TestCredentialDecryptionService : ICredentialDecryptionService
    {
        public delegate CredentialLookupStatus TryDecryptHandler(
            string accountSid,
            CredentialStore credentialStore,
            ReadOnlySpan<byte> pinDerivedKey,
            out CredentialEntry? credEntry,
            out ProtectedString? password);

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
            => Handler?.Invoke(accountSid, credentialStore, pinDerivedKey, out credEntry, out password)
                ?? throw new InvalidOperationException("Handler was not configured.");

        public CredentialLookupStatus CheckCredential(string accountSid, CredentialStore credentialStore)
            => throw new NotSupportedException();
    }
}

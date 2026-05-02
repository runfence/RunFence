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
    private readonly Mock<ICredentialDecryptionService> _credentialDecryption = new();
    private readonly Mock<IAccountPasswordService> _accountPassword = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly ProtectedBuffer _pinKey;

    private readonly AccountEditHelper _helper;

    private record TestAccountEditResult(ProtectedString? NewPassword, string? SettingsImportPath) : IAccountEditResult
    {
        public List<string> Errors { get; } = [];
    }

    public AccountEditHelperTests()
    {
        var persistenceHelper = new SessionPersistenceHelper(
            new Mock<ICredentialRepository>().Object,
            new Mock<IConfigRepository>().Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<ILoggingService>().Object);

        var firewallApplyHelper = new FirewallApplyHelper(
            new Mock<IAccountFirewallSettingsApplier>().Object,
            new DynamicPortRangeChecker(new Mock<ILoggingService>().Object, new Mock<IUserConfirmationService>().Object),
            new Mock<ILoggingService>().Object);

        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };
        _sessionProvider.Setup(s => s.GetSession()).Returns(session);

        _helper = new AccountEditHelper(
            _modalCoordinator.Object,
            persistenceHelper,
            _credentialDecryption.Object,
            _accountPassword.Object,
            new Mock<ISettingsTransferService>().Object,
            _sessionProvider.Object,
            new OperationGuard(),
            firewallApplyHelper);
    }

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void ApplyPasswordChange_StoredPassword_NonWin32Exception_Propagates()
    {
        // Arrange
        var newPwd = ProtectedString.FromChars("newpassword".AsSpan());
        var editResult = new TestAccountEditResult(newPwd, null);

        var oldPwd = new ProtectedString();
        oldPwd.AppendChar('x');

        CredentialEntry? outEntry = null;
        ProtectedString? outPwd = oldPwd;
        _credentialDecryption
            .Setup(d => d.TryDecryptCredential(TestSid, It.IsAny<CredentialStore>(), It.IsAny<byte[]>(), out outEntry, out outPwd))
            .Returns(CredentialLookupStatus.Success);

        _accountPassword
            .Setup(p => p.ChangeAccountPassword(TestSid, oldPwd, It.IsAny<ProtectedString>()))
            .Throws(new InvalidOperationException("Test non-Win32 error"));

        var credential = new CredentialEntry { Sid = TestSid, EncryptedPassword = [1, 2, 3] };
        var accountRow = new AccountRow(credential, "testuser", TestSid, hasStoredPassword: true);

        // Act & Assert: non-Win32Exception propagates because catch clause is Win32Exception-specific
        Assert.Throws<InvalidOperationException>(() =>
            _helper.ApplyPasswordChange(accountRow, editResult, isCurrentAccount: false));
    }

    [Fact]
    public void ApplyPasswordChange_StoredPassword_Win32Exception_FallsThrough()
    {
        // Arrange
        var newPwd = ProtectedString.FromChars("newpassword".AsSpan());
        var editResult = new TestAccountEditResult(newPwd, null);

        var oldPwd = new ProtectedString();
        oldPwd.AppendChar('x');

        CredentialEntry? outEntry = null;
        ProtectedString? outPwd = oldPwd;
        _credentialDecryption
            .Setup(d => d.TryDecryptCredential(TestSid, It.IsAny<CredentialStore>(), It.IsAny<byte[]>(), out outEntry, out outPwd))
            .Returns(CredentialLookupStatus.Success);

        _accountPassword
            .Setup(p => p.ChangeAccountPassword(TestSid, oldPwd, It.IsAny<ProtectedString>()))
            .Throws(new Win32Exception(86, "The specified network password is not correct."));

        // Modal coordinator does NOT execute the callback (which would show a dialog).
        // The default mock behavior returns without calling the action, so methodResult
        // stays DialogResult.None and the method returns false.

        var credential = new CredentialEntry { Sid = TestSid, EncryptedPassword = [1, 2, 3] };
        var accountRow = new AccountRow(credential, "testuser", TestSid, hasStoredPassword: true);

        // Act: Win32Exception is caught, falls through to method dialog.
        // Since RunOnSecureDesktop is mocked (no-op), methodResult remains None → returns false.
        var result = _helper.ApplyPasswordChange(accountRow, editResult, isCurrentAccount: false);

        // Assert
        Assert.False(result);
        _modalCoordinator.Verify(m => m.RunOnSecureDesktop(It.IsAny<Action>()), Times.Once);
    }
}

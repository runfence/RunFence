using System.ComponentModel;
using System.Security;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountEditHelperTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";

    private readonly Mock<IModalCoordinator> _modalCoordinator = new();
    private readonly Mock<IAccountCredentialManager> _credentialManager = new();
    private readonly Mock<IAccountPasswordService> _accountPassword = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();

    private readonly AccountEditHelper _helper;
    private readonly EditAccountDialog _dialog;

    public AccountEditHelperTests()
    {
        var persistenceHelper = new SessionPersistenceHelper(
            new Mock<ICredentialRepository>().Object,
            new Mock<IConfigRepository>().Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<ILoggingService>().Object);

        var firewallApplyHelper = new FirewallApplyHelper(
            new Mock<IAccountFirewallSettingsApplier>().Object,
            new Mock<ILoggingService>().Object);

        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
            PinDerivedKey = pinKey
        };
        _sessionProvider.Setup(s => s.GetSession()).Returns(session);

        _helper = new AccountEditHelper(
            _modalCoordinator.Object,
            persistenceHelper,
            _credentialManager.Object,
            _accountPassword.Object,
            new Mock<ISettingsTransferService>().Object,
            _sessionProvider.Object,
            new OperationGuard(),
            firewallApplyHelper);

        // Create real dialog with mocked dependencies
        var groupMembership = new Mock<ILocalGroupMembershipService>();
        var loginRestriction = new Mock<IAccountLoginRestrictionService>();
        var lsaRestriction = new Mock<IAccountLsaRestrictionService>();
        var createHandler = new EditAccountDialogCreateHandler(
            new Mock<IWindowsAccountService>().Object, groupMembership.Object,
            loginRestriction.Object, lsaRestriction.Object, new Mock<ILicenseService>().Object);
        var saveHandler = new EditAccountDialogSaveHandler(
            new Mock<IWindowsAccountService>().Object, groupMembership.Object,
            loginRestriction.Object, lsaRestriction.Object,
            new Mock<IAccountValidationService>().Object, new Mock<ILicenseService>().Object);
        var dbProvider = new Mock<IDatabaseProvider>();
        dbProvider.Setup(d => d.GetDatabase()).Returns(new AppDatabase());

        _dialog = new EditAccountDialog(
            groupMembership.Object, loginRestriction.Object, lsaRestriction.Object,
            createHandler, saveHandler, dbProvider.Object);
    }

    [Fact]
    public void ApplyPasswordChange_StoredPassword_NonWin32Exception_Propagates()
    {
        // Arrange: set NewPasswordText via reflection (private setter)
        typeof(EditAccountDialog).GetProperty("NewPasswordText")!
            .SetValue(_dialog, "newpassword");

        var oldPwd = new SecureString();
        oldPwd.AppendChar('x');

        SecureString? outPwd = oldPwd;
        _credentialManager
            .Setup(c => c.DecryptCredential(TestSid, It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>(), out outPwd))
            .Returns(CredentialLookupStatus.Success);

        _accountPassword
            .Setup(p => p.ChangeAccountPassword(TestSid, oldPwd, "newpassword"))
            .Throws(new InvalidOperationException("Test non-Win32 error"));

        var credential = new CredentialEntry { Sid = TestSid, EncryptedPassword = new byte[] { 1, 2, 3 } };
        var accountRow = new AccountRow(credential, "testuser", TestSid, hasStoredPassword: true);

        // Act & Assert: non-Win32Exception propagates because catch clause is Win32Exception-specific
        Assert.Throws<InvalidOperationException>(() =>
            _helper.ApplyPasswordChange(accountRow, _dialog, isCurrentAccount: false));
    }

    [Fact]
    public void ApplyPasswordChange_StoredPassword_Win32Exception_FallsThrough()
    {
        // Arrange: set NewPasswordText via reflection (private setter)
        typeof(EditAccountDialog).GetProperty("NewPasswordText")!
            .SetValue(_dialog, "newpassword");

        var oldPwd = new SecureString();
        oldPwd.AppendChar('x');

        SecureString? outPwd = oldPwd;
        _credentialManager
            .Setup(c => c.DecryptCredential(TestSid, It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>(), out outPwd))
            .Returns(CredentialLookupStatus.Success);

        _accountPassword
            .Setup(p => p.ChangeAccountPassword(TestSid, oldPwd, "newpassword"))
            .Throws(new Win32Exception(86, "The specified network password is not correct."));

        // Modal coordinator does NOT execute the callback (which would show a dialog).
        // The default mock behavior returns without calling the action, so methodResult
        // stays DialogResult.None and the method returns false.

        var credential = new CredentialEntry { Sid = TestSid, EncryptedPassword = new byte[] { 1, 2, 3 } };
        var accountRow = new AccountRow(credential, "testuser", TestSid, hasStoredPassword: true);

        // Act: Win32Exception is caught, falls through to method dialog.
        // Since RunOnSecureDesktop is mocked (no-op), methodResult remains None → returns false.
        var result = _helper.ApplyPasswordChange(accountRow, _dialog, isCurrentAccount: false);

        // Assert
        Assert.False(result);
        _modalCoordinator.Verify(m => m.RunOnSecureDesktop(It.IsAny<Action>()), Times.Once);
    }
}

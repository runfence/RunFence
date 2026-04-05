using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class WindowsAccountServiceTests
{
    private readonly Mock<IAccountRestrictionService> _restrictions;
    private readonly Mock<IAccountValidationService> _accountValidation;
    private readonly WindowsAccountService _service;

    public WindowsAccountServiceTests()
    {
        var log = new Mock<ILoggingService>();
        _restrictions = new Mock<IAccountRestrictionService>();
        _accountValidation = new Mock<IAccountValidationService>();
        var sidResolver = new Mock<ISidResolver>();
        _service = new WindowsAccountService(
            log.Object, _accountValidation.Object,
            _restrictions.Object, sidResolver.Object,
            new Mock<ILocalUserProvider>().Object);
    }

    // --- RenameAccount orchestration ---

    // Shared helper: invoke RenameAccount against the built-in nonexistent account name,
    // absorbing the expected InvalidOperationException so callers can focus on side-effects.
    private void AttemptRenameNonExistentAccount(
        string sid = "S-1-5-21-0-0-0-9999",
        string oldName = "DoesNotExist_RunAsMgrTest",
        string newName = "NewName")
    {
        try
        {
            _service.RenameAccount(sid, oldName, newName);
        }
        catch (InvalidOperationException)
        {
            /* expected — account doesn't exist on test machine */
        }
    }

    [Fact]
    public void RenameAccount_NonExistentAccount_ThrowsInvalidOperationException()
    {
        // NetUserSetInfo with a non-existent username returns NERR_UserNotFound (2221),
        // which must be surfaced as an InvalidOperationException (not silently swallowed).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.RenameAccount("S-1-5-21-0-0-0-9999", "DoesNotExist_RunAsMgrTest", "NewName"));

        Assert.NotEmpty(ex.Message);
    }

    // --- DeleteUser validation propagation ---

    [Fact]
    public void DeleteUser_DelegatesToValidationService_NotCurrentAccount()
    {
        var sid = "S-1-5-21-0-0-0-9999";
        _accountValidation
            .Setup(v => v.ValidateNotCurrentAccount(sid, "delete"))
            .Throws(new InvalidOperationException("Cannot delete the current account."));

        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteUser(sid));
        Assert.Contains("current account", ex.Message, StringComparison.OrdinalIgnoreCase);
        _accountValidation.Verify(v => v.ValidateNotCurrentAccount(sid, "delete"), Times.Once);
    }

    [Fact]
    public void DeleteUser_DelegatesToValidationService_NotLastAdmin()
    {
        var sid = "S-1-5-21-0-0-0-9999";
        _accountValidation
            .Setup(v => v.ValidateNotLastAdmin(sid, "delete"))
            .Throws(new InvalidOperationException("Cannot delete the last administrator account."));

        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteUser(sid));
        Assert.Contains("administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
        _accountValidation.Verify(v => v.ValidateNotLastAdmin(sid, "delete"), Times.Once);
    }

    [Fact]
    public void DeleteUser_DelegatesToValidationService_NotInteractiveUser()
    {
        var sid = "S-1-5-21-0-0-0-9999";
        _accountValidation
            .Setup(v => v.ValidateNotInteractiveUser(sid, "delete"))
            .Throws(new InvalidOperationException("Cannot delete the currently logged-in account."));

        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteUser(sid));
        Assert.Contains("logged-in", ex.Message, StringComparison.OrdinalIgnoreCase);
        _accountValidation.Verify(v => v.ValidateNotInteractiveUser(sid, "delete"), Times.Once);
    }

    // RenameAccount must always call IsAccountHidden exactly once before the rename attempt
    // (the prior hidden state is needed to restore it after a successful rename), and must
    // never call SetAccountHidden when the rename fails or when the account is not hidden.
    [Theory]
    [InlineData("S-1-5-21-0-0-0-9999", "DoesNotExist_RunAsMgrTest", "Renamed")]
    [InlineData("S-1-5-21-0-0-0-9999", "DoesNotExist_RunAsMgrTest_Alpha", "AlphaNew")]
    [InlineData("S-1-5-21-0-0-0-8888", "DoesNotExist_RunAsMgrTest_Beta", "BetaNew")]
    public void RenameAccount_NonExistentAccount_ChecksHiddenStatusOnceNeverSetsIt(
        string sid, string oldName, string newName)
    {
        _restrictions.Setup(r => r.IsAccountHidden(oldName)).Returns(false);

        AttemptRenameNonExistentAccount(sid, oldName, newName);

        _restrictions.Verify(r => r.IsAccountHidden(oldName), Times.Once);
        _restrictions.Verify(r => r.SetAccountHidden(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
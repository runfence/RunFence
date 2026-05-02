using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class WindowsAccountServiceTests
{
    private readonly Mock<IAccountLoginRestrictionService> _restrictions;
    private readonly Mock<IAccountValidationService> _accountValidation;
    private readonly Mock<ILocalUserProvider> _localUserProvider;
    private readonly Mock<ILocalSamSidResolver> _localSamSidResolver;
    private readonly Mock<ILocalAccountProvisioningService> _localAccountProvisioning;
    private readonly Mock<ILoggingService> _log;
    private readonly WindowsAccountService _service;

    public WindowsAccountServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _restrictions = new Mock<IAccountLoginRestrictionService>();
        _accountValidation = new Mock<IAccountValidationService>();
        var sidResolver = new Mock<ISidResolver>();
        _localUserProvider = new Mock<ILocalUserProvider>();
        _localSamSidResolver = new Mock<ILocalSamSidResolver>();
        _localAccountProvisioning = new Mock<ILocalAccountProvisioningService>();
        _service = new WindowsAccountService(
            _log.Object, _accountValidation.Object,
            _restrictions.Object, sidResolver.Object,
            new Mock<IProfilePathResolver>().Object,
            _localUserProvider.Object,
            _localSamSidResolver.Object,
            _localAccountProvisioning.Object,
            new Mock<IFolderHandlerService>().Object);
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
        _localAccountProvisioning
            .Setup(p => p.RenameLocalUser("DoesNotExist_RunAsMgrTest", "NewName"))
            .Returns(2221);

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

    [Fact]
    public void DeleteUser_UsesLocalAccountProvisioningServiceForSidDeletion()
    {
        var sid = "S-1-5-21-0-0-0-9999";
        var deletionFailure = new Exception("deletion stopped before profile cleanup");
        _localAccountProvisioning
            .Setup(p => p.DeleteLocalUserBySid(sid))
            .Throws(deletionFailure);

        var ex = Assert.Throws<InvalidOperationException>(() => _service.DeleteUser(sid));

        Assert.Contains("Failed to delete account", ex.Message);
        Assert.Same(deletionFailure, ex.InnerException);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserBySid(sid), Times.Once);
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
        _localAccountProvisioning.Setup(p => p.RenameLocalUser(oldName, newName)).Returns(2221);

        AttemptRenameNonExistentAccount(sid, oldName, newName);

        _restrictions.Verify(r => r.IsAccountHidden(oldName), Times.Once);
        _restrictions.Verify(r => r.SetAccountHidden(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void CreateLocalUser_ReturnsCanonicalLocalSamSid()
    {
        const string username = "newuser";
        const string localSid = "S-1-5-21-10-20-30-1001";
        using var password = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        var sequence = new MockSequence();
        _localAccountProvisioning.InSequence(sequence)
            .Setup(p => p.CreateLocalUser(username, password));
        _localUserProvider.InSequence(sequence)
            .Setup(p => p.InvalidateCache());
        _localSamSidResolver.InSequence(sequence)
            .Setup(r => r.GetRequiredLocalUserSid(username))
            .Returns(localSid);
        _localAccountProvisioning.InSequence(sequence)
            .Setup(p => p.SetDisplayName(username, username))
            .Returns(0);

        var sid = _service.CreateLocalUser(username, password);

        Assert.Equal(localSid, sid);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserByName(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CreateLocalUser_UsesLocalSamSidResolverForNameCollisionAvoidance()
    {
        const string username = "sharedname";
        const string localSid = "S-1-5-21-10-20-30-1001";
        using var password = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        _localSamSidResolver
            .Setup(r => r.GetRequiredLocalUserSid(username))
            .Returns(localSid);

        var sid = _service.CreateLocalUser(username, password);

        Assert.Equal(localSid, sid);
        _localSamSidResolver.Verify(r => r.GetRequiredLocalUserSid(username), Times.Once);
    }

    [Fact]
    public void CreateLocalUser_SidLookupFailure_DeletesCreatedAccountAndReportsOriginalFailure()
    {
        const string username = "partialuser";
        using var password = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        var lookupFailure = new InvalidOperationException("SAM lookup failed");

        _localSamSidResolver
            .Setup(r => r.GetRequiredLocalUserSid(username))
            .Throws(lookupFailure);

        var ex = Assert.Throws<InvalidOperationException>(() => _service.CreateLocalUser(username, password));

        Assert.Same(lookupFailure, ex);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserByName(username), Times.Once);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserBySid(It.IsAny<string>()), Times.Never);
        _localUserProvider.Verify(p => p.InvalidateCache(), Times.Exactly(2));
        _localAccountProvisioning.Verify(p => p.SetDisplayName(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CreateLocalUser_DisplayNameFailure_IsWarningOnlyWithoutCleanup()
    {
        const string username = "displaywarnuser";
        const string localSid = "S-1-5-21-10-20-30-1002";
        using var password = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        _localSamSidResolver
            .Setup(r => r.GetRequiredLocalUserSid(username))
            .Returns(localSid);
        _localAccountProvisioning
            .Setup(p => p.SetDisplayName(username, username))
            .Returns(2221);

        var sid = _service.CreateLocalUser(username, password);

        Assert.Equal(localSid, sid);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserByName(It.IsAny<string>()), Times.Never);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserBySid(It.IsAny<string>()), Times.Never);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("NetUserSetInfo(1011)") && s.Contains("2221"))), Times.Once);
    }

    [Fact]
    public void CreateLocalUser_DisplayNameException_IsWarningOnlyWithoutCleanup()
    {
        const string username = "displaythrowuser";
        const string localSid = "S-1-5-21-10-20-30-1003";
        using var password = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        _localSamSidResolver
            .Setup(r => r.GetRequiredLocalUserSid(username))
            .Returns(localSid);
        _localAccountProvisioning
            .Setup(p => p.SetDisplayName(username, username))
            .Throws(new InvalidOperationException("display name unavailable"));

        var sid = _service.CreateLocalUser(username, password);

        Assert.Equal(localSid, sid);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserByName(It.IsAny<string>()), Times.Never);
        _localAccountProvisioning.Verify(p => p.DeleteLocalUserBySid(It.IsAny<string>()), Times.Never);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("NetUserSetInfo(1011)") && s.Contains("display name unavailable"))), Times.Once);
    }
}

using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class EditAccountDialogCreateHandlerTests
{
    private const string CreatedSid = "S-1-5-21-1000-2000-3000-1001";
    private const string AdminGroupSid = "S-1-5-32-544";
    private const string UserGroupSid = "S-1-5-32-545";

    private readonly Mock<IWindowsAccountService> _account = new();
    private readonly Mock<ILocalGroupMembershipService> _groupMembership = new();
    private readonly Mock<IAccountRestrictionService> _restriction = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly EditAccountDialogCreateHandler _handler;

    public EditAccountDialogCreateHandlerTests()
    {
        _account.Setup(a => a.CreateLocalUser(It.IsAny<string>(), It.IsAny<string>())).Returns(CreatedSid);
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _handler = new EditAccountDialogCreateHandler(
            _account.Object, _groupMembership.Object, _restriction.Object, _licenseService.Object);
    }

    [Fact]
    public void Execute_CheckedGroups_PassesSidsToAddUserToGroups()
    {
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: "newuser",
            PasswordText: "P@ssw0rd",
            ConfirmPasswordText: "P@ssw0rd",
            IsEphemeral: false,
            CheckedGroups: [(AdminGroupSid, "Administrators")],
            UncheckedGroups: [],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        _handler.Execute(request);

        _groupMembership.Verify(g => g.AddUserToGroups(CreatedSid, "newuser",
            It.Is<List<string>>(l => l.SequenceEqual(new[] { AdminGroupSid }))), Times.Once);
    }

    [Fact]
    public void Execute_UncheckedGroups_PassesSidsToRemoveUserFromGroups()
    {
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: "newuser",
            PasswordText: "P@ssw0rd",
            ConfirmPasswordText: "P@ssw0rd",
            IsEphemeral: false,
            CheckedGroups: [],
            UncheckedGroups: [(UserGroupSid, "Users")],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        _handler.Execute(request);

        _groupMembership.Verify(g => g.RemoveUserFromGroups(CreatedSid, "newuser",
            It.Is<List<string>>(l => l.SequenceEqual(new[] { UserGroupSid }))), Times.Once);
    }

    [Fact]
    public void Execute_Success_PassesSidsNotNamesToGroupService()
    {
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: "newuser",
            PasswordText: "P@ssw0rd",
            ConfirmPasswordText: "P@ssw0rd",
            IsEphemeral: false,
            CheckedGroups: [(AdminGroupSid, "Administrators")],
            UncheckedGroups: [(UserGroupSid, "Users")],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        _handler.Execute(request);

        // AddUserToGroups receives SIDs, not display names
        _groupMembership.Verify(g => g.AddUserToGroups(CreatedSid, "newuser",
                It.Is<List<string>>(l => l.Contains(AdminGroupSid) && !l.Contains("Administrators"))),
            Times.Once);

        // RemoveUserFromGroups receives SIDs, not display names
        _groupMembership.Verify(g => g.RemoveUserFromGroups(CreatedSid, "newuser",
                It.Is<List<string>>(l => l.Contains(UserGroupSid) && !l.Contains("Users"))),
            Times.Once);
    }

    [Fact]
    public void Execute_EmptyPassword_CreatesAccountSuccessfully()
    {
        // Arrange — empty password is allowed at the handler level; UI enforces non-empty via EditAccountDialog
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: "newuser",
            PasswordText: "",
            ConfirmPasswordText: "",
            IsEphemeral: false,
            CheckedGroups: [],
            UncheckedGroups: [],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        // Act
        var result = _handler.Execute(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(CreatedSid, result.Sid);
        _account.Verify(a => a.CreateLocalUser("newuser", ""), Times.Once);
    }

    [Theory]
    [InlineData("", "pass", "pass", "1\u201320 characters")]
    [InlineData("user/bad", "pass", "pass", "invalid characters")]
    [InlineData("newuser", "pass1", "pass2", "do not match")]
    public void Execute_ValidationFailure_ReturnsNullWithExpectedError(
        string name, string pwd, string confirm, string expectedError)
    {
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: name,
            PasswordText: pwd,
            ConfirmPasswordText: confirm,
            IsEphemeral: false,
            CheckedGroups: [],
            UncheckedGroups: [],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        var result = _handler.Execute(request);

        Assert.Null(result);
        Assert.Contains(expectedError, _handler.LastValidationError);
        _account.Verify(a => a.CreateLocalUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── Restriction path tests ────────────────────────────────────────────

    private EditAccountDialogCreateHandler.CreateAccountRequest MakeRequest(
        bool allowNetworkLogin = true, bool allowLogon = true, bool allowBgAutorun = true,
        int currentHiddenCount = 0) =>
        new(Username: "newuser", PasswordText: "P@ssw0rd", ConfirmPasswordText: "P@ssw0rd",
            IsEphemeral: false, CheckedGroups: [], UncheckedGroups: [],
            AllowLogon: allowLogon, AllowNetworkLogin: allowNetworkLogin,
            AllowBgAutorun: allowBgAutorun, CurrentHiddenCount: currentHiddenCount);

    [Fact]
    public void Execute_AllowNetworkLoginFalse_CallsSetLocalOnly()
    {
        _handler.Execute(MakeRequest(allowNetworkLogin: false));

        _restriction.Verify(r => r.SetLocalOnlyBySid(CreatedSid, true), Times.Once);
    }

    [Fact]
    public void Execute_AllowNetworkLoginTrue_DoesNotCallSetLocalOnly()
    {
        _handler.Execute(MakeRequest(allowNetworkLogin: true));

        _restriction.Verify(r => r.SetLocalOnlyBySid(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Execute_AllowLogonFalse_NoLicenseService_CallsSetLoginBlocked()
    {
        // licenseService: null means no license enforcement; blocked logon should be applied directly
        _handler.Execute(MakeRequest(allowLogon: false));

        _restriction.Verify(r => r.SetLoginBlockedBySid(CreatedSid, "newuser", true), Times.Once);
    }

    [Fact]
    public void Execute_AllowLogonFalse_LicenseDenied_AddsErrorSkipsBlock()
    {
        // Arrange: license service denies hiding more accounts
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(l => l.CanHideAccount(0)).Returns(false);
        licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, 0))
            .Returns("Limit reached");
        var handler = new EditAccountDialogCreateHandler(_account.Object, _groupMembership.Object, _restriction.Object, licenseService.Object);

        var result = handler.Execute(MakeRequest(allowLogon: false));

        Assert.NotNull(result);
        Assert.Contains("Limit reached", result.Errors);
        _restriction.Verify(r => r.SetLoginBlockedBySid(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Execute_AllowBgAutorunFalse_CallsSetNoBgAutostart()
    {
        _handler.Execute(MakeRequest(allowBgAutorun: false));

        _restriction.Verify(r => r.SetNoBgAutostartBySid(CreatedSid, true), Times.Once);
    }

    [Fact]
    public void Execute_AllowBgAutorunTrue_DoesNotCallSetNoBgAutostart()
    {
        _handler.Execute(MakeRequest(allowBgAutorun: true));

        _restriction.Verify(r => r.SetNoBgAutostartBySid(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Execute_RestrictionThrows_CollectsErrorInResult()
    {
        // Arrange: SetLocalOnlyBySid throws; user was already created
        _restriction.Setup(r => r.SetLocalOnlyBySid(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new Exception("LSA error"));

        var result = _handler.Execute(MakeRequest(allowNetworkLogin: false));

        Assert.NotNull(result);
        Assert.Equal(CreatedSid, result.Sid);
        Assert.Contains("LSA error", result.Errors[0]);
    }

    [Fact]
    public void Execute_GroupAddThrows_CollectsErrorInResult()
    {
        // Arrange: AddUserToGroups throws
        _groupMembership.Setup(g => g.AddUserToGroups(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
            .Throws(new Exception("Group not found"));

        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: "newuser", PasswordText: "P@ssw0rd", ConfirmPasswordText: "P@ssw0rd",
            IsEphemeral: false,
            CheckedGroups: [("S-1-5-32-544", "Administrators")],
            UncheckedGroups: [],
            AllowLogon: true, AllowNetworkLogin: true, AllowBgAutorun: true,
            CurrentHiddenCount: 0);

        var result = _handler.Execute(request);

        Assert.NotNull(result);
        Assert.Contains("Group not found", result.Errors[0]);
    }
}
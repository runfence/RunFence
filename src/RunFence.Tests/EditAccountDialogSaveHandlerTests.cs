using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class EditAccountDialogSaveHandlerTests
{
    private const string UserSid = "S-1-5-21-1000-2000-3000-1001";
    private const string AdminGroupSid = "S-1-5-32-544";
    private const string UserGroupSid = "S-1-5-32-545";

    private readonly Mock<IWindowsAccountService> _account = new();
    private readonly Mock<ILocalGroupMembershipService> _groupMembership = new();
    private readonly Mock<IAccountLoginRestrictionService> _loginRestriction = new();
    private readonly Mock<IAccountLsaRestrictionService> _lsaRestriction = new();
    private readonly Mock<IAccountValidationService> _validation = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly EditAccountDialogSaveHandler _handler;

    public EditAccountDialogSaveHandlerTests()
    {
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _handler = new EditAccountDialogSaveHandler(
            _account.Object, _groupMembership.Object, _loginRestriction.Object, _lsaRestriction.Object, _validation.Object, _licenseService.Object);
    }

    [Fact]
    public void Execute_PassesSidsToAddUserToGroups()
    {
        var request = new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "alice",
            GroupsToAdd: [AdminGroupSid],
            GroupsToRemove: [],
            AdminGroupSid: AdminGroupSid,
            NewNetworkLogin: null, NewLogon: null, NewBgAutorun: null,
            CurrentHiddenCount: 0, NoLogonState: null);

        _handler.Execute(request);

        _groupMembership.Verify(g => g.AddUserToGroups(UserSid, "alice",
            It.Is<List<string>>(l => l.Contains(AdminGroupSid))), Times.Once);
    }

    [Fact]
    public void Execute_PassesSidsToRemoveUserFromGroups()
    {
        var request = new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "alice",
            GroupsToAdd: [],
            GroupsToRemove: [UserGroupSid],
            AdminGroupSid: AdminGroupSid,
            NewNetworkLogin: null, NewLogon: null, NewBgAutorun: null,
            CurrentHiddenCount: 0, NoLogonState: null);

        _handler.Execute(request);

        _groupMembership.Verify(g => g.RemoveUserFromGroups(UserSid, "alice",
            It.Is<List<string>>(l => l.Contains(UserGroupSid))), Times.Once);
    }

    [Fact]
    public void Execute_NameChanged_CallsRenameAccount()
    {
        var request = new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "bob",
            GroupsToAdd: [],
            GroupsToRemove: [],
            AdminGroupSid: AdminGroupSid,
            NewNetworkLogin: null, NewLogon: null, NewBgAutorun: null,
            CurrentHiddenCount: 0, NoLogonState: null);

        var result = _handler.Execute(request);

        _account.Verify(a => a.RenameAccount(UserSid, "alice", "bob"), Times.Once);
        Assert.Equal("bob", result.NewUsername);
    }

    [Fact]
    public void Execute_AdminGroupSid_ValidationChecksWithSid()
    {
        // AdminGroupSid is used as a SID value; when that SID is in GroupsToRemove, validation runs
        _validation.Setup(v => v.ValidateNotCurrentAccount(UserSid, It.IsAny<string>()))
            .Throws(new InvalidOperationException("Cannot remove current account from Administrators"));

        var request = new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "alice",
            GroupsToAdd: [],
            GroupsToRemove: [AdminGroupSid],
            AdminGroupSid: AdminGroupSid,
            NewNetworkLogin: null, NewLogon: null, NewBgAutorun: null,
            CurrentHiddenCount: 0, NoLogonState: null);

        var result = _handler.Execute(request);

        // Validation failure → ValidationError returned, no OS mutations
        Assert.NotNull(result.ValidationError);
        _groupMembership.Verify(g => g.RemoveUserFromGroups(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>>()), Times.Never);
    }

    // ── Restriction path tests ────────────────────────────────────────────

    private EditAccountDialogSaveHandler.SaveAccountRequest MakeRequest(
        bool? newNetworkLogin = null, bool? newLogon = null, bool? newBgAutorun = null,
        int currentHiddenCount = 0, bool? noLogonState = null) =>
        new(Sid: UserSid, CurrentUsername: "alice", NewName: "alice",
            GroupsToAdd: [], GroupsToRemove: [], AdminGroupSid: null,
            NewNetworkLogin: newNetworkLogin, NewLogon: newLogon, NewBgAutorun: newBgAutorun,
            CurrentHiddenCount: currentHiddenCount, NoLogonState: noLogonState);

    [Fact]
    public void Execute_NewNetworkLoginFalse_CallsSetLocalOnly()
    {
        _handler.Execute(MakeRequest(newNetworkLogin: false));

        _lsaRestriction.VerifySetLocalOnly(UserSid, true);
    }

    [Fact]
    public void Execute_NewNetworkLoginNull_DoesNotCallSetLocalOnly()
    {
        _handler.Execute(MakeRequest(newNetworkLogin: null));

        _lsaRestriction.VerifySetLocalOnlyNeverCalled();
    }

    [Fact]
    public void Execute_NewNetworkLoginTrue_CallsSetLocalOnlyFalse()
    {
        // R2_TL4: NewNetworkLogin=true means removing the restriction (localOnly=false)
        _handler.Execute(MakeRequest(newNetworkLogin: true));

        _lsaRestriction.VerifySetLocalOnly(UserSid, false);
    }

    [Fact]
    public void Execute_NewLogonFalse_NoLicenseService_CallsSetLoginBlocked()
    {
        // licenseService: null — no enforcement; block should be applied directly
        _handler.Execute(MakeRequest(newLogon: false));

        _loginRestriction.VerifySetLoginBlocked(UserSid, "alice", true);
    }

    [Fact]
    public void Execute_NewLogonFalse_LicenseDenied_NotAlreadyBlocked_AddsError()
    {
        // Arrange: license service denies; account not already blocked (NoLogonState: false)
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(l => l.CanHideAccount(0)).Returns(false);
        licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, 0))
            .Returns("Limit");
        var handler = new EditAccountDialogSaveHandler(
            _account.Object, _groupMembership.Object, _loginRestriction.Object, _lsaRestriction.Object, _validation.Object, licenseService.Object);

        var result = handler.Execute(MakeRequest(newLogon: false, noLogonState: false));

        Assert.Contains("Limit", result.Errors);
        _loginRestriction.VerifySetLoginBlockedNeverCalled();
    }

    [Fact]
    public void Execute_NewLogonFalse_LicenseDenied_AlreadyBlocked_BypassesLicenseCheck()
    {
        // Arrange: same license mock, but NoLogonState: true (already blocked) — license check skipped
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(l => l.CanHideAccount(0)).Returns(false);
        licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, 0))
            .Returns("Limit");
        var handler = new EditAccountDialogSaveHandler(
            _account.Object, _groupMembership.Object, _loginRestriction.Object, _lsaRestriction.Object, _validation.Object, licenseService.Object);

        var result = handler.Execute(MakeRequest(newLogon: false, noLogonState: true));

        // NoLogonState == true means condition `request.NoLogonState != true` is false → license skipped
        Assert.DoesNotContain("Limit", result.Errors);
        _loginRestriction.VerifySetLoginBlocked(UserSid, "alice", true);
    }

    [Fact]
    public void Execute_NewBgAutorunFalse_CallsSetNoBgAutostart()
    {
        _handler.Execute(MakeRequest(newBgAutorun: false));

        _lsaRestriction.VerifySetNoBgAutostart(UserSid, true);
    }

    [Fact]
    public void Execute_NewBgAutorunNull_DoesNotCallSetNoBgAutostart()
    {
        _handler.Execute(MakeRequest(newBgAutorun: null));

        _lsaRestriction.VerifySetNoBgAutostartNeverCalled();
    }

    [Fact]
    public void Execute_ValidateNotLastAdmin_ThrowsValidationError()
    {
        // Arrange: removing from admin group triggers ValidateNotLastAdmin which throws
        _validation.Setup(v => v.ValidateNotLastAdmin(UserSid, It.IsAny<string>()))
            .Throws(new InvalidOperationException("Last admin"));

        var request = new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid, CurrentUsername: "alice", NewName: "alice",
            GroupsToAdd: [], GroupsToRemove: [AdminGroupSid],
            AdminGroupSid: AdminGroupSid,
            NewNetworkLogin: null, NewLogon: null, NewBgAutorun: null,
            CurrentHiddenCount: 0, NoLogonState: null);

        var result = _handler.Execute(request);

        Assert.NotNull(result.ValidationError);
        Assert.Contains("Last admin", result.ValidationError);
        _groupMembership.Verify(g => g.RemoveUserFromGroups(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>>()), Times.Never);
    }

    [Fact]
    public void Execute_RestrictionThrows_CollectsNonFatalError()
    {
        // Arrange: SetLocalOnlyBySid throws; should be non-fatal (collected in Errors)
        _lsaRestriction.Setup(r => r.SetLocalOnlyBySid(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new Exception("LSA error"));

        var result = _handler.Execute(MakeRequest(newNetworkLogin: false));

        Assert.Null(result.ValidationError);
        Assert.Contains("LSA error", result.Errors[0]);
    }
}
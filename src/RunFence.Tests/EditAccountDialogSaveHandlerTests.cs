using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class EditAccountDialogSaveHandlerTests
{
    private const string UserSid = "S-1-5-21-1000-2000-3000-1001";
    private readonly Mock<IWindowsAccountService> _account = new();
    private readonly Mock<ILocalGroupMutationService> _groupMembership = new();
    private readonly Mock<IAccountLsaRestrictionService> _lsaRestriction = new();
    private readonly Mock<IAccountRestrictionCoordinator> _restrictionCoordinator = new();
    private readonly Mock<IAccountValidationService> _validation = new();
    private readonly Mock<ILicenseService> _licenseService = new();

    [Fact]
    public void Execute_GroupsAndRestrictions_UsesExpectedBoundaries()
    {
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _restrictionCoordinator.Setup(c => c.ApplyRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new AccountRestrictionResult(
            [
                new AccountRestrictionEntry(AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Succeeded, false, null)
            ]));
        _lsaRestriction.Setup(r => r.GetLocalOnlyState(UserSid)).Returns(false);
        _lsaRestriction.Setup(r => r.GetNoBgAutostartState(UserSid)).Returns(false);

        var handler = new EditAccountDialogSaveHandler(
            _account.Object,
            _groupMembership.Object,
            _lsaRestriction.Object,
            _restrictionCoordinator.Object,
            _validation.Object,
            _licenseService.Object);

        var result = handler.Execute(new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "alice",
            GroupsToAdd: ["S-1-5-32-544"],
            GroupsToRemove: ["S-1-5-32-545"],
            AdminGroupSid: null,
            NewNetworkLogin: false,
            NewLogon: false,
            NewBgAutorun: false,
            CurrentHiddenCount: 0,
            NoLogonState: false));

        Assert.Null(result.ValidationError);
        Assert.Equal(EditAccountDialogSaveHandler.SaveAccountStatus.Saved, result.Status);
        _groupMembership.Verify(g => g.AddUserToGroups(UserSid, "alice", It.IsAny<List<string>>()), Times.Once);
        _groupMembership.Verify(g => g.RemoveUserFromGroups(UserSid, "alice", It.IsAny<List<string>>()), Times.Once);
        _restrictionCoordinator.Verify(c => c.ApplyRestrictions(UserSid, "alice", true, true, true), Times.Once);
    }

    [Fact]
    public void Execute_HiddenLicenseDenied_StillAppliesNetworkAndBackgroundRestrictions()
    {
        _licenseService.Setup(l => l.CanHideAccount(3)).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, 3))
            .Returns("hidden limit");
        _restrictionCoordinator.Setup(c => c.ApplyRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new AccountRestrictionResult(
            [
                new AccountRestrictionEntry(AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Succeeded, false, null),
                new AccountRestrictionEntry(AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Succeeded, false, null)
            ]));
        _lsaRestriction.Setup(r => r.GetLocalOnlyState(UserSid)).Returns(false);
        _lsaRestriction.Setup(r => r.GetNoBgAutostartState(UserSid)).Returns(false);

        var handler = new EditAccountDialogSaveHandler(
            _account.Object,
            _groupMembership.Object,
            _lsaRestriction.Object,
            _restrictionCoordinator.Object,
            _validation.Object,
            _licenseService.Object);

        var result = handler.Execute(new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "alice",
            GroupsToAdd: [],
            GroupsToRemove: [],
            AdminGroupSid: null,
            NewNetworkLogin: false,
            NewLogon: false,
            NewBgAutorun: false,
            CurrentHiddenCount: 3,
            NoLogonState: false));

        Assert.Equal(EditAccountDialogSaveHandler.SaveAccountStatus.SavedWithWarnings, result.Status);
        Assert.Contains("hidden limit", result.Errors);
        _restrictionCoordinator.Verify(c => c.ApplyRestrictions(UserSid, "alice", false, true, true), Times.Once);
    }

    [Fact]
    public void Execute_RestrictionFailure_UsesSharedFormatterOutput()
    {
        var entry = new AccountRestrictionEntry(
            AccountRestrictionKind.NetworkLogin,
            AccountRestrictionStatus.Failed,
            false,
            "network policy failure");

        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _restrictionCoordinator.Setup(c => c.ApplyRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(new AccountRestrictionResult([entry]));
        _lsaRestriction.Setup(r => r.GetLocalOnlyState(UserSid)).Returns(false);
        _lsaRestriction.Setup(r => r.GetNoBgAutostartState(UserSid)).Returns(false);

        var handler = new EditAccountDialogSaveHandler(
            _account.Object,
            _groupMembership.Object,
            _lsaRestriction.Object,
            _restrictionCoordinator.Object,
            _validation.Object,
            _licenseService.Object);

        var result = handler.Execute(new EditAccountDialogSaveHandler.SaveAccountRequest(
            Sid: UserSid,
            CurrentUsername: "alice",
            NewName: "alice",
            GroupsToAdd: [],
            GroupsToRemove: [],
            AdminGroupSid: null,
            NewNetworkLogin: false,
            NewLogon: true,
            NewBgAutorun: true,
            CurrentHiddenCount: 0,
            NoLogonState: false));

        Assert.Equal(EditAccountDialogSaveHandler.SaveAccountStatus.SavedWithWarnings, result.Status);
        Assert.Contains(AccountRestrictionEntryFormatter.Format(entry), result.Errors);
    }
}

using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class EditAccountDialogCreateHandlerTests
{
    private const string CreatedSid = "S-1-5-21-1000-2000-3000-1001";

    private static EditAccountDialogCreateHandler CreateHandler(
        IWindowsAccountService account,
        ILocalGroupMutationService groups,
        IAccountRestrictionCoordinator restrictions,
        ILicenseService license,
        IDatabaseService? databaseService = null,
        ISidNameCacheService? sidNameCache = null)
    {
        var database = new AppDatabase();
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(i => i.Invoke(It.IsAny<Action>())).Callback<Action>(a => a());

        return new EditAccountDialogCreateHandler(
            account,
            groups,
            restrictions,
            license,
            uiThreadInvoker.Object,
            Mock.Of<IAppStateProvider>(s => s.Database == database),
            session,
            databaseService ?? Mock.Of<IDatabaseService>(),
            sidNameCache ?? Mock.Of<ISidNameCacheService>());
    }

    [Fact]
    public void Execute_AppliesGroupAndRestrictionFlows()
    {
        var account = new Mock<IWindowsAccountService>();
        var groups = new Mock<ILocalGroupMutationService>();
        var restrictions = new Mock<IAccountRestrictionCoordinator>();
        var license = new Mock<ILicenseService>();
        account.Setup(a => a.CreateLocalUser("newuser", It.IsAny<ProtectedString>())).Returns(CreatedSid);
        license.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        restrictions.Setup(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true))
            .Returns(SucceededRestrictionResult());

        var handler = CreateHandler(account.Object, groups.Object, restrictions.Object, license.Object);
        var request = BuildRequest(
            checkedGroups: [("S-1-5-32-544", "Administrators")],
            uncheckedGroups: [("S-1-5-32-545", "Users")]);

        var result = handler.Execute(request);

        Assert.Equal(CreateAccountStatus.Succeeded, result.Status);
        Assert.NotNull(result.RollbackState);
        Assert.Equal(CreatedSid, result.RollbackState!.Sid);
        Assert.Equal("newuser", result.RollbackState.Username);
        Assert.False(result.RollbackState.HadPreviousAccount);
        groups.Verify(g => g.AddUserToGroups(CreatedSid, "newuser", It.IsAny<List<string>>()), Times.Once);
        groups.Verify(g => g.RemoveUserFromGroups(CreatedSid, "newuser", It.IsAny<List<string>>()), Times.Once);
        restrictions.Verify(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true), Times.Once);
    }

    [Fact]
    public void Execute_GroupAddFailure_StillAppliesRestrictions()
    {
        var account = new Mock<IWindowsAccountService>();
        var groups = new Mock<ILocalGroupMutationService>();
        var restrictions = new Mock<IAccountRestrictionCoordinator>();
        var license = new Mock<ILicenseService>();
        account.Setup(a => a.CreateLocalUser("newuser", It.IsAny<ProtectedString>())).Returns(CreatedSid);
        license.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        groups.Setup(g => g.AddUserToGroups(CreatedSid, "newuser", It.IsAny<List<string>>()))
            .Throws(new InvalidOperationException("add failed"));
        restrictions.Setup(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true))
            .Returns(SucceededRestrictionResult());

        var handler = CreateHandler(account.Object, groups.Object, restrictions.Object, license.Object);
        var request = BuildRequest(checkedGroups: [("S-1-5-32-544", "Administrators")]);

        var result = handler.Execute(request);

        Assert.Equal(CreateAccountStatus.Succeeded, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("Group membership", StringComparison.Ordinal));
        restrictions.Verify(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true), Times.Once);
    }

    [Fact]
    public void Execute_GroupRemoveFailure_StillAppliesRestrictions()
    {
        var account = new Mock<IWindowsAccountService>();
        var groups = new Mock<ILocalGroupMutationService>();
        var restrictions = new Mock<IAccountRestrictionCoordinator>();
        var license = new Mock<ILicenseService>();
        account.Setup(a => a.CreateLocalUser("newuser", It.IsAny<ProtectedString>())).Returns(CreatedSid);
        license.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        groups.Setup(g => g.RemoveUserFromGroups(CreatedSid, "newuser", It.IsAny<List<string>>()))
            .Throws(new InvalidOperationException("remove failed"));
        restrictions.Setup(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true))
            .Returns(SucceededRestrictionResult());

        var handler = CreateHandler(account.Object, groups.Object, restrictions.Object, license.Object);
        var request = BuildRequest(uncheckedGroups: [("S-1-5-32-545", "Users")]);

        var result = handler.Execute(request);

        Assert.Equal(CreateAccountStatus.Succeeded, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("Group removal", StringComparison.Ordinal));
        restrictions.Verify(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true), Times.Once);
    }

    [Fact]
    public void Execute_HiddenLicenseDenied_StillAttemptsNetworkAndBackgroundRestrictions()
    {
        var account = new Mock<IWindowsAccountService>();
        var groups = new Mock<ILocalGroupMutationService>();
        var restrictions = new Mock<IAccountRestrictionCoordinator>();
        var license = new Mock<ILicenseService>();
        account.Setup(a => a.CreateLocalUser("newuser", It.IsAny<ProtectedString>())).Returns(CreatedSid);
        license.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(false);
        license.Setup(l => l.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, 3)).Returns("Hidden accounts are not allowed.");
        restrictions.Setup(r => r.ApplyRestrictions(CreatedSid, "newuser", false, true, true))
            .Returns(SucceededRestrictionResult());

        var handler = CreateHandler(account.Object, groups.Object, restrictions.Object, license.Object);
        var request = BuildRequest(
            allowLogon: false,
            allowNetworkLogin: false,
            allowBgAutorun: false,
            currentHiddenCount: 3);

        var result = handler.Execute(request);

        Assert.Equal(CreateAccountStatus.Succeeded, result.Status);
        Assert.Contains("Hidden accounts are not allowed.", result.Errors);
        restrictions.Verify(r => r.ApplyRestrictions(CreatedSid, "newuser", false, true, true), Times.Once);
    }

    [Fact]
    public void Execute_SaveConfigFailsAfterWindowsAccountCreation_ReturnsCleanupStateSaveFailedAndSkipsFurtherSetup()
    {
        var account = new Mock<IWindowsAccountService>();
        var groups = new Mock<ILocalGroupMutationService>();
        var restrictions = new Mock<IAccountRestrictionCoordinator>();
        var license = new Mock<ILicenseService>();
        var databaseService = new Mock<IDatabaseService>();
        account.Setup(a => a.CreateLocalUser("newuser", It.IsAny<ProtectedString>())).Returns(CreatedSid);
        databaseService
            .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));

        var handler = CreateHandler(account.Object, groups.Object, restrictions.Object, license.Object, databaseService.Object);

        var result = handler.Execute(BuildRequest());

        Assert.Equal(CreateAccountStatus.CleanupStateSaveFailed, result.Status);
        Assert.Equal(CreatedSid, result.Sid);
        Assert.Null(result.Password);
        Assert.Equal("save failed", result.ErrorMessage);
        Assert.NotNull(result.RollbackState);
        Assert.Equal(CreatedSid, result.RollbackState!.Sid);
        groups.Verify(g => g.AddUserToGroups(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
        restrictions.Verify(r => r.ApplyRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Execute_PersistsCreatedLocalAccountNameWithMachinePrefix()
    {
        var account = new Mock<IWindowsAccountService>();
        var groups = new Mock<ILocalGroupMutationService>();
        var restrictions = new Mock<IAccountRestrictionCoordinator>();
        var license = new Mock<ILicenseService>();
        var sidNameCache = new Mock<ISidNameCacheService>();

        account.Setup(a => a.CreateLocalUser("newuser", It.IsAny<ProtectedString>())).Returns(CreatedSid);
        license.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        restrictions.Setup(r => r.ApplyRestrictions(CreatedSid, "newuser", true, true, true))
            .Returns(SucceededRestrictionResult());

        var handler = CreateHandler(
            account.Object,
            groups.Object,
            restrictions.Object,
            license.Object,
            sidNameCache: sidNameCache.Object);

        var result = handler.Execute(BuildRequest());

        Assert.Equal(CreateAccountStatus.Succeeded, result.Status);
        sidNameCache.Verify(
            c => c.UpdateName(CreatedSid, $"{Environment.MachineName}\\newuser"),
            Times.Once);
    }

    private static AccountRestrictionResult SucceededRestrictionResult() =>
        new(
        [
            new AccountRestrictionEntry(AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, false, null),
            new AccountRestrictionEntry(AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, false, null),
            new AccountRestrictionEntry(AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Succeeded, false, null),
            new AccountRestrictionEntry(AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, false, null),
            new AccountRestrictionEntry(AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Succeeded, false, null)
        ]);

    private static EditAccountDialogCreateHandler.CreateAccountRequest BuildRequest(
        List<(string Sid, string Name)>? checkedGroups = null,
        List<(string Sid, string Name)>? uncheckedGroups = null,
        bool allowLogon = false,
        bool allowNetworkLogin = false,
        bool allowBgAutorun = false,
        int currentHiddenCount = 0) =>
        new(
            Username: "newuser",
            Password: ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            ConfirmPassword: ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            IsEphemeral: false,
            CheckedGroups: checkedGroups ?? [],
            UncheckedGroups: uncheckedGroups ?? [],
            AllowLogon: allowLogon,
            AllowNetworkLogin: allowNetworkLogin,
            AllowBgAutorun: allowBgAutorun,
            CurrentHiddenCount: currentHiddenCount);
}

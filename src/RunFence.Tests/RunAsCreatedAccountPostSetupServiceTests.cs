using Moq;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.RunAs;
using RunFence.Security;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class RunAsCreatedAccountPostSetupServiceTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    [Fact]
    public async Task CompleteAsync_NoPermissionPromptNeeded_AppliesDefaultsAndReturnsWarnings()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var databaseService = new Mock<IDatabaseService>();
        var settingsApplier = CreateSettingsApplier(appState.Object, databaseService.Object);
        var permissionPromptHelper = new Mock<IRunAsPermissionPromptHelper>();
        permissionPromptHelper.Setup(p => p.PromptIfNeeded(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((AncestorPermissionResult?)null);

        var service = new RunAsCreatedAccountPostSetupService(
            appState.Object,
            settingsApplier,
            permissionPromptHelper.Object);

        var errors = new List<string> { "Settings import: warning" };
        var result = await service.CompleteAsync(
            new RunAsCreatedAccountPostSetupRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                FilePath = @"C:\Apps\tool.exe",
                Errors = errors,
                IsEphemeral = true,
                SelectedPrivilegeLevel = PrivilegeLevel.Basic
            },
            new CredentialEntry { Id = Guid.NewGuid(), Sid = sid });

        Assert.False(result.WasCanceled);
        Assert.Null(result.PermissionGrant);
        Assert.Equal(errors, result.WarningMessages);
        var account = Assert.Single(database.Accounts);
        Assert.Equal(sid, account.Sid);
        Assert.Equal(PrivilegeLevel.Basic, account.PrivilegeLevel);
        Assert.NotNull(account.DeleteAfterUtc);
        databaseService.Verify(
            s => s.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Once);
        Assert.Single(errors);
    }

    [Fact]
    public async Task CompleteAsync_PermissionPromptCanceled_ReturnsCanceledWithoutMutatingRequestErrors()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var settingsApplier = CreateSettingsApplier(appState.Object, Mock.Of<IDatabaseService>());
        var permissionPromptHelper = new Mock<IRunAsPermissionPromptHelper>();
        permissionPromptHelper.Setup(p => p.PromptIfNeeded(It.IsAny<string>(), It.IsAny<string>()))
            .Throws<OperationCanceledException>();

        var service = new RunAsCreatedAccountPostSetupService(
            appState.Object,
            settingsApplier,
            permissionPromptHelper.Object);

        var originalErrors = new List<string> { "existing warning" };
        var result = await service.CompleteAsync(
            new RunAsCreatedAccountPostSetupRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                FilePath = @"C:\Apps\tool.exe",
                Errors = originalErrors,
                IsEphemeral = false,
                SelectedPrivilegeLevel = PrivilegeLevel.Isolated
            },
            new CredentialEntry { Id = Guid.NewGuid(), Sid = sid });

        Assert.True(result.WasCanceled);
        Assert.Null(result.PermissionGrant);
        Assert.Equal(originalErrors, result.WarningMessages);
        Assert.Single(originalErrors);
        Assert.Equal(PrivilegeLevel.Isolated, database.GetAccount(sid)?.PrivilegeLevel);
    }

    private RunAsAccountSettingsApplier CreateSettingsApplier(
        IAppStateProvider appState,
        IDatabaseService databaseService)
    {
        var session = new SessionContext
{
            Database = appState.Database,
            CredentialStore = new CredentialStore { ArgonSalt = [1, 2, 3] },
        }.WithOwnedPinDerivedKey(_pinKey);
        _sessions.Add(session);

        var firewallApplyHelper = new FirewallApplyHelper(
            Mock.Of<IAccountFirewallSettingsApplier>(),
            new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), Mock.Of<IUserConfirmationService>(), new StandardNetshCommandRunner()),
            Mock.Of<ILoggingService>());

        return new RunAsAccountSettingsApplier(
            appState,
            session,
            databaseService,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISettingsTransferService>(),
            firewallApplyHelper,
            new ImmediateAccountCreationProgressRunner());
    }
}

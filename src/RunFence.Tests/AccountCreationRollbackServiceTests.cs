using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AccountCreationRollbackServiceTests
{
    [Fact]
    public async Task RollbackAsync_Success_RestoresPreviousSettingsAfterExecutorRuns()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        database.Settings.HasShownFirstAccountWarning = true;
        database.Settings.HasShownUsersGroupWarning = true;

        var previousSettings = new AppSettings();
        var credentialStore = new CredentialStore();

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);
        lifecycleManager.Setup(s => s.ClearAccountRestrictions(
                sid,
                "newuser",
                It.Is<AppSettings>(settings =>
                    settings.HasShownFirstAccountWarning &&
                    settings.HasShownUsersGroupWarning)))
            .Verifiable();

        var service = new AccountCreationRollbackService(
            new CreatedAccountRollbackExecutor(
                lifecycleManager.Object,
                Mock.Of<IAccountCredentialManager>(),
                Mock.Of<IAssociationAutoSetService>(),
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILoggingService>()));

        var rollbackState = new AccountCreationRollbackState
        {
            CreatedAccount = new CreatedAccountRollbackState
            {
                Sid = sid,
                Username = "newuser",
                HadPreviousAccount = false,
                HadPreviousSidName = false,
                HadPreviousFirewallSettings = false
            },
            PreviousSettings = previousSettings,
        };

        await service.RollbackAsync(rollbackState, database, credentialStore);

        Assert.False(database.Settings.HasShownFirstAccountWarning);
        Assert.False(database.Settings.HasShownUsersGroupWarning);
        lifecycleManager.Verify();
    }

    [Fact]
    public async Task RollbackAsync_DeleteFails_DoesNotRestorePreviousSettings()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        database.Settings.HasShownFirstAccountWarning = true;
        var credentialStore = new CredentialStore();

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(false, sid, "delete failed"));

        var service = new AccountCreationRollbackService(
            new CreatedAccountRollbackExecutor(
                lifecycleManager.Object,
                Mock.Of<IAccountCredentialManager>(),
                Mock.Of<IAssociationAutoSetService>(),
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILoggingService>()));

        var rollbackState = new AccountCreationRollbackState
        {
            CreatedAccount = new CreatedAccountRollbackState
            {
                Sid = sid,
                Username = "newuser",
                HadPreviousAccount = false,
                HadPreviousSidName = false,
                HadPreviousFirewallSettings = false
            },
            PreviousSettings = new AppSettings()
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.RollbackAsync(rollbackState, database, credentialStore));

        Assert.Equal("delete failed", ex.Message);
        Assert.True(database.Settings.HasShownFirstAccountWarning);
    }
}

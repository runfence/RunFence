using Moq;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.OrphanedProfiles;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AccountDeletionOrchestratorTests : IDisposable
{
    private const string Sid = "S-1-5-21-0-0-0-2001";
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void DeleteUser_DelegatesPreflightBeforeDeletingAccount()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var callOrder = new List<string>();
            var validationCalls = 0;
            var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
            lifecycleManager.Setup(m => m.ValidateDeleteAsync(Sid))
                .ReturnsAsync(() =>
                {
                    if (validationCalls++ == 0)
                        callOrder.Add("preflight");
                    return AccountDeleteValidationResult.Success;
                });

            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);
            var sessionProvider = new Mock<ISessionProvider>(MockBehavior.Strict);
            sessionProvider.Setup(s => s.GetSession()).Returns(session);

            var messageBoxService = new Mock<IAccountMessageBoxService>(MockBehavior.Strict);
            messageBoxService.Setup(m => m.Show(
                    null,
                    It.IsAny<string>(),
                    "Confirm Delete Account",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1))
                .Returns(DialogResult.Yes);

            var accountDeletion = new Mock<IAccountDeletionService>(MockBehavior.Strict);
            accountDeletion.Setup(d => d.DeleteAccountAsync(Sid, "targetuser", session.CredentialStore, false))
                .Callback(() => callOrder.Add("delete"))
                .ReturnsAsync(AccountDeletionCleanupResult.Success());

            var localUserProvider = new Mock<ILocalUserProvider>(MockBehavior.Strict);
            localUserProvider.Setup(p => p.InvalidateCache());

            bool saveAndRefreshRaised = false;
            var orchestrator = new AccountDeletionOrchestrator(
                lifecycleManager.Object,
                new AccountMigrationOrchestrator(
                    new Mock<IModalCoordinator>(MockBehavior.Strict).Object,
                    null!,
                    new Mock<IOrphanedProfileService>(MockBehavior.Strict).Object,
                    new Mock<IAccountLifecycleManager>(MockBehavior.Strict).Object,
                    new Mock<IAccountMessageBoxService>(MockBehavior.Strict).Object),
                sessionProvider.Object,
                new OperationGuard(),
                new Mock<ISidResolver>(MockBehavior.Strict).Object,
                new Mock<IProfilePathResolver>(MockBehavior.Strict).Object,
                new AccountDeletionPreflightService(
                    lifecycleManager.Object,
                    new Mock<IProcessTerminationService>(MockBehavior.Strict).Object,
                    new Mock<IAccountMessageBoxService>(MockBehavior.Strict).Object),
                accountDeletion.Object,
                new Mock<ITrayBalloonService>(MockBehavior.Strict).Object,
                localUserProvider.Object,
                messageBoxService.Object);
            orchestrator.SaveAndRefreshRequested += (_, _) => saveAndRefreshRaised = true;

            orchestrator.DeleteUser(new AccountRow(null, "targetuser", Sid, hasStoredPassword: false), 0);

            StaTestHelper.PumpUntil(() => saveAndRefreshRaised);

            Assert.Equal(["preflight", "delete"], callOrder);
            lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Exactly(2));
            accountDeletion.Verify(d => d.DeleteAccountAsync(Sid, "targetuser", session.CredentialStore, false), Times.Once);
            localUserProvider.Verify(p => p.InvalidateCache(), Times.Once);
        });
    }
}

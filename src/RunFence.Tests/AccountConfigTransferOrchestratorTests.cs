using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AccountConfigTransferOrchestrator"/>.
///
/// Coverage gap: Most post-PIN-dialog behaviors cannot be unit-tested because the orchestrator
/// constructs <see cref="RunFence.Startup.UI.Forms.PinDialog"/> directly (not injectable) and
/// calls <see cref="System.Windows.Forms.Form.ShowDialog"/>, which blocks until the user
/// interacts with the dialog. The variable <c>completed</c> in
/// <see cref="AccountConfigTransferOrchestrator.RunAsync"/> is a local closure variable —
/// the only way to advance it to <c>true</c> is to call the action passed to
/// <see cref="IModalCoordinator.RunOnSecureDesktop"/>, which triggers the blocking
/// <c>ShowDialog</c>. Not calling the action leaves <c>completed = false</c> and the method
/// returns early. All post-PIN paths are therefore inaccessible in automated unit tests.
///
/// Note: account selection (GetAvailableAccounts) is now the caller's responsibility;
/// RunAsync receives the pre-selected targetSid and targetDisplayName directly.
///
/// Scenarios not covered here (require integration or manual testing):
/// - MigrateToAccount called with the correct store snapshot, SID, password, and key
/// - Error logging and MessageBox when MigrateToAccount throws
/// - DeleteCurrentAccountData + onExit invoked when user confirms post-migration
/// - App continues running when user declines post-migration deletion
/// </summary>
public class AccountConfigTransferOrchestratorTests : IDisposable
{
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<IModalCoordinator> _modalCoordinator = new();
    private readonly Mock<IAccountConfigMigrationService> _migrationService = new();
    private readonly Mock<ILocalGroupMembershipService> _localGroupMembership = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<ILoggingService> _log = new();

    private readonly ProtectedBuffer _pinKey;
    private readonly SessionContext _session;

    public AccountConfigTransferOrchestratorTests()
    {
        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore
            {
                ArgonSalt = new byte[32],
                EncryptedCanary = [1, 2, 3],
                Credentials = []
            },
            PinDerivedKey = _pinKey
        };
    }

    public void Dispose() => _pinKey.Dispose();

    private AccountConfigTransferOrchestrator CreateOrchestrator() =>
        new(_pinService.Object, _modalCoordinator.Object, _migrationService.Object,
            _localGroupMembership.Object, _sidNameCache.Object, _encryptionService.Object,
            _log.Object);

    // ── PIN verification cancelled ────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ReturnsSilently_WhenPinVerificationCancelled()
    {
        // Arrange: RunOnSecureDesktop does NOT invoke its action, so completed stays false
        // and the orchestrator returns early.
        _modalCoordinator.Setup(m => m.RunOnSecureDesktop(It.IsAny<Action>()));

        var orchestrator = CreateOrchestrator();
        var exitCalled = false;

        // Act
        await orchestrator.RunAsync(_session, "S-1-5-21-test-sid", "Test Account", () => { exitCalled = true; });

        // Assert: RunOnSecureDesktop was called exactly once (PIN always prompted)
        _modalCoordinator.Verify(m => m.RunOnSecureDesktop(It.IsAny<Action>()), Times.Once);

        // Assert: no migration, no exit after PIN cancellation
        _migrationService.Verify(m => m.MigrateToAccount(
            It.IsAny<CredentialStore>(), It.IsAny<string>(),
            It.IsAny<ProtectedString>(), It.IsAny<byte[]>()), Times.Never);
        _migrationService.Verify(m => m.DeleteCurrentAccountData(), Times.Never);
        Assert.False(exitCalled);
    }
}

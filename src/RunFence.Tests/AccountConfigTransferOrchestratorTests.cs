using System.Runtime.InteropServices;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class AccountConfigTransferOrchestratorTests : IDisposable
{
    private const string TargetSid = "S-1-5-21-test-sid";

    private readonly Mock<IAccountConfigMigrationService> _migrationService = new();
    private readonly Mock<ILocalGroupMembershipService> _localGroupMembership = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly ICredentialEncryptionSpanService _encryptionService = new CredentialEncryptionService(new NativeDpapiProtector());
    private readonly Mock<ILoggingService> _log = new();

    private readonly StubSecureDesktopService _secureDesktopService = new();
    private readonly StubPromptService _promptService = new();

    private readonly SecureSecret _pinKey;
    private readonly SessionContext _session;

    public AccountConfigTransferOrchestratorTests()
    {
        _pinKey = TestSecretFactory.Create(32);
        _session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore
            {
                ArgonSalt = new byte[32],
                EncryptedCanary = [1, 2, 3],
                Credentials = []
            },
        }.WithOwnedPinDerivedKey(_pinKey);
    }

    public void Dispose() => _session.Dispose();

    private AccountConfigTransferOrchestrator CreateOrchestrator() =>
        new(
            _secureDesktopService,
            _promptService,
            _migrationService.Object,
            _localGroupMembership.Object,
            _sidNameCache.Object,
            _encryptionService,
            _log.Object);

    [Fact]
    public async Task RunAsync_DoesNotMigrate_WhenAuthorizationIsCanceled()
    {
        _secureDesktopService.StoredCredentialResult = new AccountConfigTransferAuthorizationResult(
            Completed: false,
            CapturedPassword: null,
            ReplacementStore: _session.CredentialStore);
        _session.CredentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = TargetSid,
            EncryptedPassword = [1]
        });

        await CreateOrchestrator().RunAsync(_session, TargetSid, () => { });

        _migrationService.Verify(m => m.MigrateToAccount(
            It.IsAny<CredentialStore>(), It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_StoredCredentialFlow_InvokesMigrationAndExitsAfterDelete()
    {
        using var decryptedPassword = CreatePassword("stored-secret");
        var encryptedPassword = _session.PinDerivedKey.TransformSnapshot(key => _encryptionService.Encrypt(decryptedPassword, key));
        _session.CredentialStore.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = TargetSid,
            EncryptedPassword = encryptedPassword
        });
        _secureDesktopService.StoredCredentialResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: null,
            ReplacementStore: CredentialStoreCloneHelper.CloneStore(_session.CredentialStore));
        _promptService.ConfirmDeleteCurrentDataResult = true;

        string? migratedPassword = null;
        _migrationService.Setup(m => m.MigrateToAccount(
                It.IsAny<CredentialStore>(), TargetSid, It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, string, ProtectedString, ISecureSecretSnapshotSource>((_, _, password, _) =>
                migratedPassword = ProtectedStringToString(password));

        var exitCalled = false;
        await CreateOrchestrator().RunAsync(_session, TargetSid, () => exitCalled = true);

        Assert.Equal("stored-secret", migratedPassword);
        Assert.True(exitCalled);
        _migrationService.Verify(m => m.DeleteCurrentAccountData(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_TypedPasswordFlow_UsesCapturedPasswordAndKeepsCurrentData_WhenCleanupDeclined()
    {
        var capturedPassword = CreatePassword("typed-secret");
        _secureDesktopService.TypedPasswordResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: capturedPassword,
            ReplacementStore: CredentialStoreCloneHelper.CloneStore(_session.CredentialStore));
        _promptService.ConfirmDeleteCurrentDataResult = false;

        string? migratedPassword = null;
        _migrationService.Setup(m => m.MigrateToAccount(
                It.IsAny<CredentialStore>(), TargetSid, It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, string, ProtectedString, ISecureSecretSnapshotSource>((_, _, password, _) =>
                migratedPassword = ProtectedStringToString(password));

        var exitCalled = false;
        await CreateOrchestrator().RunAsync(_session, TargetSid, () => exitCalled = true);

        Assert.Equal("typed-secret", migratedPassword);
        Assert.False(exitCalled);
        _migrationService.Verify(m => m.DeleteCurrentAccountData(), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ShortCircuitsWhenUserDeclinesOverwrite()
    {
        _secureDesktopService.TypedPasswordResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: CreatePassword("typed-secret"),
            ReplacementStore: CredentialStoreCloneHelper.CloneStore(_session.CredentialStore));
        _migrationService.Setup(m => m.TargetHasExistingData(TargetSid)).Returns(true);
        _promptService.ConfirmOverwriteExistingDataResult = false;

        await CreateOrchestrator().RunAsync(_session, TargetSid, () => { });

        Assert.Equal(1, _promptService.ConfirmOverwriteExistingDataCalls);
        _migrationService.Verify(m => m.MigrateToAccount(
            It.IsAny<CredentialStore>(), It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ShowsPrompt_WhenMigrationThrows()
    {
        var failure = new InvalidOperationException("boom");
        _secureDesktopService.TypedPasswordResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: CreatePassword("typed-secret"),
            ReplacementStore: CredentialStoreCloneHelper.CloneStore(_session.CredentialStore));
        _migrationService.Setup(m => m.MigrateToAccount(
                It.IsAny<CredentialStore>(), It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(failure);

        await CreateOrchestrator().RunAsync(_session, TargetSid, () => { });

        Assert.Same(failure, _promptService.LastMigrationFailure);
        _migrationService.Verify(m => m.DeleteCurrentAccountData(), Times.Never);
    }

    [Fact]
    public async Task RunAsync_CallsExitWhenCleanupFails()
    {
        var failure = new IOException("cleanup failed");
        _secureDesktopService.TypedPasswordResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: CreatePassword("typed-secret"),
            ReplacementStore: CredentialStoreCloneHelper.CloneStore(_session.CredentialStore));
        _promptService.ConfirmDeleteCurrentDataResult = true;
        _migrationService.Setup(m => m.DeleteCurrentAccountData()).Throws(failure);

        var exitCalled = false;
        await CreateOrchestrator().RunAsync(_session, TargetSid, () => exitCalled = true);

        Assert.True(exitCalled);
        Assert.Same(failure, _promptService.LastCleanupFailure);
    }

    [Fact]
    public async Task RunAsync_CopiesOnlyDetachedStoreSnapshotIntoMigrationCall()
    {
        var sourceStore = new CredentialStore
        {
            ArgonSalt = [1, 2, 3, 4],
            EncryptedCanary = [5, 6, 7],
            Credentials =
            [
                new CredentialEntry
                {
                    Id = Guid.NewGuid(),
                    Sid = TargetSid,
                    EncryptedPassword = [9, 10]
                }
            ]
        };
        _session.CredentialStore = sourceStore;
        var replacementStore = CredentialStoreCloneHelper.CloneStore(sourceStore);
        _secureDesktopService.StoredCredentialResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: null,
            ReplacementStore: replacementStore);
        using var decryptedPassword = CreatePassword("stored-secret");
        replacementStore.Credentials[0].EncryptedPassword = _session.PinDerivedKey.TransformSnapshot(
            key => _encryptionService.Encrypt(decryptedPassword, key));
        _promptService.ConfirmDeleteCurrentDataResult = false;
        CredentialStore? migratedStore = null;
        _migrationService.Setup(m => m.MigrateToAccount(
                It.IsAny<CredentialStore>(), It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, string, ProtectedString, ISecureSecretSnapshotSource>((store, _, _, _) => migratedStore = store);

        await CreateOrchestrator().RunAsync(_session, TargetSid, () => { });

        Assert.NotNull(migratedStore);
        Assert.NotSame(sourceStore, migratedStore);
        Assert.NotSame(sourceStore.Credentials, migratedStore!.Credentials);
    }

    [Fact]
    public async Task RunAsync_CapturedDisposedCurrentKey_ShowsMigrationFailedInsteadOfThrowing()
    {
        using var decryptedPassword = CreatePassword("stored-secret");
        var encryptedPassword = _session.PinDerivedKey.TransformSnapshot(
            key => _encryptionService.Encrypt(decryptedPassword, key));
        _session.CredentialStore.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = TargetSid,
            EncryptedPassword = encryptedPassword
        });
        _secureDesktopService.StoredCredentialResult = new AccountConfigTransferAuthorizationResult(
            Completed: true,
            CapturedPassword: null,
            ReplacementStore: CredentialStoreCloneHelper.CloneStore(_session.CredentialStore));
        _secureDesktopService.BeforeReturn = () =>
            _session.ReplacePinDerivedKey(new SecureSecret(32, data => data.Fill(9)));

        var exception = await Record.ExceptionAsync(() => CreateOrchestrator().RunAsync(_session, TargetSid, () => { }));

        Assert.Null(exception);
        Assert.IsType<ObjectDisposedException>(_promptService.LastMigrationFailure);
        _migrationService.Verify(m => m.MigrateToAccount(
            It.IsAny<CredentialStore>(), It.IsAny<string>(), It.IsAny<ProtectedString>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    private static ProtectedString CreatePassword(string value) => new(value.AsSpan(), protect: false);

    private static string ProtectedStringToString(ProtectedString password)
    {
        return password.UseUnicodeSnapshot(snapshot =>
            Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty);
    }

    private sealed class StubSecureDesktopService : IAccountConfigTransferSecureDesktopService
    {
        public AccountConfigTransferAuthorizationResult? StoredCredentialResult { get; set; }
        public AccountConfigTransferAuthorizationResult? TypedPasswordResult { get; set; }
        public Action? BeforeReturn { get; set; }

        public AccountConfigTransferAuthorizationResult AuthorizeStoredCredentialTransfer(
            CredentialStore clonedStore,
            string targetAccountSid)
        {
            BeforeReturn?.Invoke();
            return StoredCredentialResult ?? new AccountConfigTransferAuthorizationResult(true, null, clonedStore);
        }

        public AccountConfigTransferAuthorizationResult AuthorizeTypedPasswordTransfer(
            CredentialStore clonedStore,
            string targetAccountSid)
        {
            BeforeReturn?.Invoke();
            return TypedPasswordResult ?? new AccountConfigTransferAuthorizationResult(true, null, clonedStore);
        }
    }

    private sealed class StubPromptService : IAccountConfigTransferPromptService
    {
        public bool ConfirmOverwriteExistingDataResult { get; set; } = true;
        public bool ConfirmDeleteCurrentDataResult { get; set; } = true;
        public int ConfirmOverwriteExistingDataCalls { get; private set; }
        public Exception? LastMigrationFailure { get; private set; }
        public Exception? LastCleanupFailure { get; private set; }

        public bool ConfirmOverwriteExistingData(string targetAccountSid)
        {
            ConfirmOverwriteExistingDataCalls++;
            return ConfirmOverwriteExistingDataResult;
        }

        public void ShowMigrationFailed(string targetAccountSid, Exception error) => LastMigrationFailure = error;

        public bool ConfirmDeleteCurrentData(string targetAccountSid) => ConfirmDeleteCurrentDataResult;

        public void ShowCleanupFailed(string targetAccountSid, Exception error) => LastCleanupFailure = error;
    }
}

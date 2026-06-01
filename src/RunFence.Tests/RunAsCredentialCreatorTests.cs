using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class RunAsCredentialCreatorTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void PersistCredential_SaveFails_ThrowsRollbackExceptionWithCredentialId()
    {
        using var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);

        var encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
        encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Returns([1, 2, 3]);

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(s => s.SaveCredentialStore(session.CredentialStore))
            .Throws(new InvalidOperationException("save failed"));

        var localUserProvider = new Mock<ILocalUserProvider>();
        var sidNameCache = new Mock<ISidNameCacheService>();

        var service = new RunAsCredentialCreator(
            session,
            new ByteArrayCredentialEncryptionSpanAdapter(encryptionService.Object),
            databaseService.Object,
            localUserProvider.Object,
            sidNameCache.Object);

        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = "S-1-5-21-1-2-3-1001",
            Username = "newuser",
            HadPreviousAccount = false,
            HadPreviousSidName = false,
            HadPreviousFirewallSettings = false
        };

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var ex = Assert.Throws<RunAsCredentialPersistenceException>(() =>
            service.PersistCredential(password, rollbackState.Sid, rollbackState.Username, rollbackState));

        Assert.Same(rollbackState, ex.RollbackState);
        Assert.Equal("save failed", ex.SaveException.Message);
        Assert.NotNull(rollbackState.CredentialId);
        Assert.Single(session.CredentialStore.Credentials);
        sidNameCache.Verify(s => s.ResolveAndCache(rollbackState.Sid, rollbackState.Username), Times.Once);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
    }

    [Fact]
    public void PersistCredential_EncryptedPassword_RoundTripsWithSnapshotKey()
    {
        byte[] pinKeyBytes = new byte[32];
        for (int i = 0; i < pinKeyBytes.Length; i++)
            pinKeyBytes[i] = (byte)(i + 1);

        using var roundTripPinKey = TestSecretFactory.FromBytes(pinKeyBytes.ToArray());
        using var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(roundTripPinKey);

        var encryptionService = new CredentialEncryptionService(new NativeDpapiProtector());
        var databaseService = new Mock<IDatabaseService>();
        var service = new RunAsCredentialCreator(
            session,
            encryptionService,
            databaseService.Object,
            new Mock<ILocalUserProvider>().Object,
            new Mock<ISidNameCacheService>().Object);

        using var password = ProtectedString.FromChars("P@ssw0rd!".AsSpan());
        var credential = service.PersistCredential(
            password,
            "S-1-5-21-1-2-3-1001",
            "newuser",
            new CreatedAccountRollbackState
            {
                Sid = "S-1-5-21-1-2-3-1001",
                Username = "newuser",
                HadPreviousAccount = false,
                HadPreviousSidName = false,
                HadPreviousFirewallSettings = false
            });
        using var decrypted = encryptionService.Decrypt(credential.EncryptedPassword, pinKeyBytes);

        Assert.True(ProtectedString.ContentEqual(password, decrypted));
        databaseService.Verify(s => s.SaveCredentialStore(session.CredentialStore), Times.Once);
    }
}

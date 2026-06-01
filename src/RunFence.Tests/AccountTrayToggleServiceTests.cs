using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountTrayToggleServiceTests : IDisposable
{
    private const string TestSid = "S-1-5-21-123-456-789-1001";

    private readonly Mock<IMainConfigPersistence> _configRepository = new();
    private readonly Mock<IAssociationAutoSetService> _associationAutoSetService = new();
    private readonly Mock<IInputInjectionBlockerService> _inputInjectionBlocker = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly SessionContext _session;

    public AccountTrayToggleServiceTests()
    {
        _session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = [1, 2, 3] },
        }.WithClonedPinDerivedKey(_pinKey);
        _sessionProvider.Setup(s => s.GetSession()).Returns(_session);
        _associationAutoSetService.Setup(s => s.AutoSetForUser(It.IsAny<string>()))
            .Returns(AssociationAutoSetResult.Success);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
    }

    private AccountTrayToggleService CreateService()
    {
        return new AccountTrayToggleService(
            new SessionPersistenceHelper(
                Mock.Of<IConfigReencryptionPersistence>(),
                _configRepository.Object,
                Mock.Of<ISidNameCacheService>(),
                () => new InlineUiThreadInvoker(action => action()),
                Mock.Of<ILoggingService>()),
            _sessionProvider.Object,
            _associationAutoSetService.Object,
            _inputInjectionBlocker.Object);
    }

    [Fact]
    public void ToggleManageAssociations_Enable_SavesDefaultStateBeforeAutoSet()
    {
        _session.Database.GetOrCreateAccount(TestSid).ManageAssociations = false;
        var events = new List<string>();
        _configRepository
            .Setup(r => r.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((db, _, _) =>
                events.Add($"save:{db.GetAccount(TestSid)?.ManageAssociations}"));
        _associationAutoSetService
            .Setup(s => s.AutoSetForUser(TestSid))
            .Callback(() => events.Add($"autoset:{_session.Database.GetAccount(TestSid)?.ManageAssociations}"))
            .Returns(AssociationAutoSetResult.Success);

        var service = CreateService();
        service.ToggleManageAssociations(TestSid, () => events.Add("saved"));

        Assert.Equal(["save:", "autoset:", "saved"], events);
        Assert.Null(_session.Database.GetAccount(TestSid));
    }

    [Fact]
    public void ToggleManageAssociations_Disable_RestoresBeforeSavingFalse()
    {
        _session.Database.GetOrCreateAccount(TestSid).ManageAssociations = true;
        var events = new List<string>();
        _associationAutoSetService
            .Setup(s => s.RestoreForUser(TestSid))
            .Callback(() => events.Add($"restore:{_session.Database.GetAccount(TestSid)?.ManageAssociations}"));
        _configRepository
            .Setup(r => r.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((db, _, _) =>
                events.Add($"save:{db.GetAccount(TestSid)?.ManageAssociations}"));

        var service = CreateService();
        service.ToggleManageAssociations(TestSid, () => events.Add("saved"));

        Assert.Equal(["restore:True", "save:False", "saved"], events);
        Assert.False(_session.Database.GetAccount(TestSid)?.ManageAssociations);
    }
}

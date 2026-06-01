using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class OptionsSettingsHandlerTests
{
    [Fact]
    public void SaveSettings_UsesPinDerivedKeySnapshotSource()
    {
        using var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = [1, 2, 3] },
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var configRepository = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
        var sessionProvider = new Mock<ISessionProvider>(MockBehavior.Strict);
        sessionProvider.Setup(provider => provider.GetSession()).Returns(session);

        configRepository
            .Setup(repository => repository.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt));

        var handler = new OptionsSettingsHandler(configRepository.Object, sessionProvider.Object);
        var settingsChanged = false;

        handler.SaveSettings(() => settingsChanged = true);

        Assert.True(settingsChanged);
        sessionProvider.Verify(provider => provider.GetSession(), Times.Once);
        configRepository.VerifyAll();
    }

    [Fact]
    public void SaveCallerChanges_UsesPinDerivedKeySnapshotSource_AndInvokesCallbacks()
    {
        using var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = [4, 5, 6] },
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var configRepository = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
        var sessionProvider = new Mock<ISessionProvider>(MockBehavior.Strict);
        sessionProvider.Setup(provider => provider.GetSession()).Returns(session);

        configRepository
            .Setup(repository => repository.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt));

        var handler = new OptionsSettingsHandler(configRepository.Object, sessionProvider.Object);
        var refreshed = false;
        var dataChanged = false;

        handler.SaveCallerChanges(
            () => refreshed = true,
            () => dataChanged = true);

        Assert.True(refreshed);
        Assert.True(dataChanged);
        sessionProvider.Verify(provider => provider.GetSession(), Times.Once);
        configRepository.VerifyAll();
    }

    [Fact]
    public void SaveSettings_AfterKeyReplacement_UsesReplacementPinDerivedKey()
    {
        using var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = [7, 8, 9] },
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var initialKeySource = session.PinDerivedKey;
        session.ReplacePinDerivedKey(new SecureSecret(32, data => data.Fill(0x5A)));

        var configRepository = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
        var sessionProvider = new Mock<ISessionProvider>(MockBehavior.Strict);
        sessionProvider.Setup(provider => provider.GetSession()).Returns(session);

        configRepository
            .Setup(repository => repository.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt));

        var handler = new OptionsSettingsHandler(configRepository.Object, sessionProvider.Object);

        handler.SaveSettings(() => { });

        Assert.NotSame(initialKeySource, session.PinDerivedKey);
        sessionProvider.Verify(provider => provider.GetSession(), Times.Once);
        configRepository.VerifyAll();
    }
}
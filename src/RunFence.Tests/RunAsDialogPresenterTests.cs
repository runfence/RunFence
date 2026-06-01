using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.RunAs.UI.Forms;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsDialogPresenterTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public async Task ShowRunAsDialogAsync_WhenLockedAndCallerIsAdmin_ConsumesStartupUnlockGrant()
    {
        var database = new AppDatabase();
        using var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);

        var modalCoordinator = new Mock<IModalCoordinator>();
        var appState = new Mock<IAppStateProvider>();
        var appLock = new Mock<IAppLockControl>();
        var startupUnlockGrant = new Mock<IStartupUnlockGrant>();

        appState.Setup(a => a.Database).Returns(database);
        appLock.Setup(l => l.IsLocked).Returns(true);
        appLock.Setup(l => l.TryUnlockForOperationWithResultAsync(true)).ReturnsAsync(OperationUnlockResult.Succeeded);
        startupUnlockGrant.Setup(g => g.TryConsume()).Returns(true);

        var presenter = new RunAsDialogPresenter(
            modalCoordinator.Object,
            new RunAsPermissionChecker(new Mock<IAclPermissionService>().Object),
            new RunAsCredentialPersister(
                appState.Object,
                session,
                new ByteArrayCredentialEncryptionSpanAdapter(new Mock<IByteArrayCredentialEncryptionService>().Object),
                new Mock<IDatabaseService>().Object,
                new Mock<IDatabaseService>().Object,
                new Mock<ILoggingService>().Object),
            appState.Object,
            appLock.Object,
            startupUnlockGrant.Object,
            session,
            new RunAsPostDialogRouter(
                new Mock<IRunAsUserAccountCreator>().Object,
                new Mock<IRunAsContainerCreator>().Object,
                new Mock<IEvaluationLimitHelper>().Object,
                new RunAsDosProtection(new Mock<IStopwatchProvider>().Object)),
            () => null!);

        var result = await presenter.ShowRunAsDialogAsync(
            @"C:\tools\app.exe",
            arguments: null,
            shortcutContext: null,
            initialAccountSid: null,
            isAdmin: true,
            setUnlockedForRunAs: _ => { },
            useSecureDesktop: false);

        Assert.NotNull(result);
        startupUnlockGrant.Verify(g => g.TryConsume(), Times.Once);
        appLock.Verify(l => l.TryUnlockForOperationWithResultAsync(true), Times.Once);
    }
}

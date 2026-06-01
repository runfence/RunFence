using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.RunAs.UI.Forms;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsDosProtectionTests : IDisposable
{
    private long _currentTimestamp;
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    private RunAsDosProtection CreateProtection()
    {
        _currentTimestamp = 0;
        var stopwatch = new StubStopwatchProvider(
            getTimestamp: () => _currentTimestamp,
            getElapsedSeconds: (start, end) => end - start);
        return new RunAsDosProtection(stopwatch);
    }

    private sealed class StubStopwatchProvider(Func<long> getTimestamp, Func<long, long, double> getElapsedSeconds) : IStopwatchProvider
    {
        public long GetTimestamp() => getTimestamp();
        public double GetElapsedSeconds(long startTimestamp, long endTimestamp) => getElapsedSeconds(startTimestamp, endTimestamp);
    }

    private RunAsDialogPresenter CreatePresenter(
        RunAsDosProtection dosProtection,
        Mock<IAppLockControl> appLock)
    {
        var database = new AppDatabase();
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);

        var appState = new Mock<IAppStateProvider>();
        appState.Setup(a => a.Database).Returns(database);

        return new RunAsDialogPresenter(
            new Mock<IModalCoordinator>().Object,
            new RunAsPermissionChecker(new Mock<RunFence.Acl.Permissions.IAclPermissionService>().Object),
            new RunAsCredentialPersister(
                appState.Object,
                session,
                new ByteArrayCredentialEncryptionSpanAdapter(new Mock<IByteArrayCredentialEncryptionService>().Object),
                new Mock<IDatabaseService>().Object,
                new Mock<IDatabaseService>().Object,
                new Mock<ILoggingService>().Object),
            appState.Object,
            appLock.Object,
            new Mock<IStartupUnlockGrant>().Object,
            session,
            new RunAsPostDialogRouter(
                new Mock<IRunAsUserAccountCreator>().Object,
                new Mock<IRunAsContainerCreator>().Object,
                new Mock<RunFence.Licensing.IEvaluationLimitHelper>().Object,
                dosProtection),
            () => null!);
    }

    [Fact]
    public void IsBlocked_NeverDeclined_ReturnsFalse()
    {
        var dos = CreateProtection();

        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_OneDecline_ImmediatelyAfterDecline_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 100;
        dos.RecordDecline();

        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_ThreeDeclinesWithinWindow_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 100;
        dos.RecordDecline();
        _currentTimestamp = 101;
        dos.RecordDecline();
        _currentTimestamp = 102;
        dos.RecordDecline();

        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_FourDeclines_Within2min_ReturnsTrue()
    {
        var dos = CreateProtection();

        // Record 4 declines spread across time but within 2 min window
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        _currentTimestamp = 61;
        Assert.True(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_FourDeclines_After2min_ReturnsFalse()
    {
        var dos = CreateProtection();

        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        _currentTimestamp = 180;
        Assert.False(dos.IsBlocked());
    }

    [Fact]
    public void IsBlocked_WindowExpired_NewDecline_ResetsCount()
    {
        var dos = CreateProtection();

        // Record 4 declines at t=0..60 → blocked
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 20;
        dos.RecordDecline();
        _currentTimestamp = 40;
        dos.RecordDecline();
        _currentTimestamp = 60;
        dos.RecordDecline();

        _currentTimestamp = 61;
        Assert.True(dos.IsBlocked()); // 4 declines in 2 min

        // Advance past window (>120s from first decline at t=0)
        _currentTimestamp = 180;
        dos.RecordDecline(); // 5th decline — window resets, count=1
        Assert.False(dos.IsBlocked()); // only 1 decline in new window
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var dos = CreateProtection();
        _currentTimestamp = 0;
        dos.RecordDecline();
        _currentTimestamp = 1;
        dos.RecordDecline();
        _currentTimestamp = 2;
        dos.RecordDecline();
        _currentTimestamp = 3;
        dos.RecordDecline();

        // Verify blocked
        _currentTimestamp = 4;
        Assert.True(dos.IsBlocked());

        // Reset and verify unblocked
        dos.Reset();
        Assert.False(dos.IsBlocked());
    }

    [Theory]
    [InlineData(OperationUnlockResult.Declined, true)]
    [InlineData(OperationUnlockResult.Failed, true)]
    [InlineData(OperationUnlockResult.Unavailable, false)]
    public async Task RunAsUnlockOutcome_CountsOnlyDeclineAndAuthFailure(
        OperationUnlockResult unlockResult,
        bool shouldBlockAfterFourAttempts)
    {
        var dos = CreateProtection();
        var appLock = new Mock<IAppLockControl>();
        appLock.SetupGet(l => l.IsLocked).Returns(true);
        appLock.Setup(l => l.TryUnlockForOperationWithResultAsync(It.IsAny<bool>()))
            .ReturnsAsync(unlockResult);

        var presenter = CreatePresenter(dos, appLock);

        for (var i = 0; i < 4; i++)
        {
            _currentTimestamp = i;
            var result = await presenter.ShowRunAsDialogAsync(
                @"C:\tools\app.exe",
                arguments: null,
                shortcutContext: null,
                initialAccountSid: null,
                isAdmin: false,
                setUnlockedForRunAs: _ => { },
                useSecureDesktop: false);
            Assert.Null(result);
        }

        _currentTimestamp = 5;
        Assert.Equal(shouldBlockAfterFourAttempts, dos.IsBlocked());
    }
}

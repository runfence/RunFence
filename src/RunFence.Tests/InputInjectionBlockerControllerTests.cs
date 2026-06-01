using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Security.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class InputInjectionBlockerControllerTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly Mock<IInputInjectionBlockerService> _blocker = new();
    private readonly Mock<IInputInjectionTraySink> _traySink = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<ISessionSaver> _sessionSaver = new();
    private readonly Mock<IApplicationDataChangeSource> _dataChangeSource = new();
    private readonly Mock<IInputInjectionDisableBlockingDialogService> _dialogService = new();
    private readonly SessionContext _session;
    private readonly InputInjectionBlockerController _controller;

    public InputInjectionBlockerControllerTests()
    {
        var database = new AppDatabase();
        database.Settings.BlockInputInjection = true;
        database.Accounts.Add(new AccountEntry { Sid = "S-1-5-21-1", ReceiveInjectedInput = true });
        database.Accounts.Add(new AccountEntry { Sid = "S-1-5-21-2", ReceiveInjectedInput = false });
        _session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);
        _sessionProvider.Setup(s => s.GetSession()).Returns(_session);
        _controller = new InputInjectionBlockerController(
            _blocker.Object,
            _traySink.Object,
            _sessionProvider.Object,
            _sessionSaver.Object,
            _dataChangeSource.Object,
            _dialogService.Object);
    }

    public void Dispose()
    {
        _controller.Dispose();
        _pinKey.Dispose();
    }

    [Fact]
    public void Initialize_AppliesSettingAndExemptedSids()
    {
        _controller.Initialize();

        _blocker.Verify(b => b.ApplyConfigSetting(true), Times.Once);
        _blocker.Verify(b => b.UpdateExemptedSids(
            It.Is<IReadOnlyCollection<string>>(sids =>
                sids.Count == 1 && sids.Contains("S-1-5-21-1"))), Times.Once);
    }

    [Fact]
    public void DataChanged_ReappliesSettingAndExemptedSids()
    {
        _controller.Initialize();
        _session.Database.Settings.BlockInputInjection = false;
        _session.Database.Accounts[1].ReceiveInjectedInput = true;

        _dataChangeSource.Raise(source => source.DataChanged += null);

        _blocker.Verify(b => b.ApplyConfigSetting(false), Times.Once);
        _blocker.Verify(b => b.UpdateExemptedSids(
            It.Is<IReadOnlyCollection<string>>(sids =>
                sids.Count == 2
                && sids.Contains("S-1-5-21-1")
                && sids.Contains("S-1-5-21-2"))), Times.Once);
    }

    [Fact]
    public void Toggle_WhenEnabledAndUntilRestart_DisablesTemporarilyAndRefreshesTray()
    {
        _controller.Initialize();
        _blocker.SetupGet(b => b.IsEnabled).Returns(true);
        _dialogService.Setup(d => d.Show()).Returns(DisableBlockingChoice.UntilRestart);

        _traySink.Raise(sink => sink.InputInjectionToggleRequested += null);

        _blocker.Verify(b => b.SetTemporarilyDisabled(), Times.Once);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        _traySink.Verify(s => s.UpdateDatabase(_session.CredentialStore), Times.Once);
    }

    [Fact]
    public void Toggle_WhenEnabledAndPermanent_DisablesConfigSavesAndRefreshesTray()
    {
        _controller.Initialize();
        _blocker.SetupGet(b => b.IsEnabled).Returns(true);
        _dialogService.Setup(d => d.Show()).Returns(DisableBlockingChoice.Permanently);

        _traySink.Raise(sink => sink.InputInjectionToggleRequested += null);

        Assert.False(_session.Database.Settings.BlockInputInjection);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        _blocker.Verify(b => b.ApplyConfigSetting(false), Times.Once);
        _traySink.Verify(s => s.UpdateDatabase(_session.CredentialStore), Times.Once);
    }

    [Fact]
    public void Toggle_WhenDisabled_EnablesConfigSavesAndRefreshesTray()
    {
        _controller.Initialize();
        _blocker.SetupGet(b => b.IsEnabled).Returns(false);
        _session.Database.Settings.BlockInputInjection = false;

        _traySink.Raise(sink => sink.InputInjectionToggleRequested += null);

        Assert.True(_session.Database.Settings.BlockInputInjection);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        _blocker.Verify(b => b.ReEnable(), Times.Once);
        _blocker.Verify(b => b.ApplyConfigSetting(true), Times.Exactly(2)); // init + toggle
        _traySink.Verify(s => s.UpdateDatabase(_session.CredentialStore), Times.Once);
    }

    [Fact]
    public void Toggle_WhenDialogCancelled_DoesNotMutateOrRefreshTray()
    {
        _controller.Initialize();
        _blocker.SetupGet(b => b.IsEnabled).Returns(true);
        _dialogService.Setup(d => d.Show()).Returns(DisableBlockingChoice.Cancelled);

        _traySink.Raise(sink => sink.InputInjectionToggleRequested += null);

        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        _blocker.Verify(b => b.SetTemporarilyDisabled(), Times.Never);
        _blocker.Verify(b => b.SetTimedDisable(It.IsAny<TimeSpan>()), Times.Never);
        _blocker.Verify(b => b.ApplyConfigSetting(false), Times.Never);
        _traySink.Verify(s => s.UpdateDatabase(It.IsAny<CredentialStore>()), Times.Never);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTrayAndDataChanged()
    {
        _controller.Initialize();
        _controller.Dispose();
        _blocker.Invocations.Clear();

        _traySink.Raise(sink => sink.InputInjectionToggleRequested += null);
        _dataChangeSource.Raise(source => source.DataChanged += null);

        _blocker.Verify(b => b.SetTemporarilyDisabled(), Times.Never);
        _blocker.Verify(b => b.ApplyConfigSetting(It.IsAny<bool>()), Times.Never);
    }
}

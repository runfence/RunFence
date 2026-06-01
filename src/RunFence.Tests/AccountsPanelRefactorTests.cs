using System.Drawing;
using System.Windows.Forms;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AccountsPanelRefactorTests
{
    [Fact]
    public async Task AccountsPanelLifecycleCoordinator_MethodsRequiringView_ThrowBeforeInitialize()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);

        Assert.Throws<InvalidOperationException>(() => coordinator.Initialize());
        Assert.Throws<InvalidOperationException>(() => coordinator.OnVisibleChanged(true));
        Assert.Throws<InvalidOperationException>(() => coordinator.StartProcessRefresh());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RefreshOnActivationAsync());
    }

    [Fact]
    public void AccountsPanelLifecycleCoordinator_FirstInitialize_StartsInitialRefreshAndRefreshServices()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var view = new FakeAccountsPanelLifecycleView
        {
            Visible = true,
            ParentFormVisible = true
        };
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);
        coordinator.Initialize(view);

        coordinator.Initialize();

        Assert.Equal(1, view.InitialRefreshCallCount);
        Assert.Equal(1, timerCoordinator.StartCallCount);
        Assert.Equal(1, processRefreshController.StartCallCount);
        Assert.NotNull(processRefreshController.VisibilityProbe);
        Assert.True(processRefreshController.VisibilityProbe!());

        view.Visible = false;
        Assert.False(processRefreshController.VisibilityProbe());
    }

    [Fact]
    public async Task AccountsPanelLifecycleCoordinator_OnVisibleChanged_RefreshesOnlyWhenVisibleAndIdle()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var view = new FakeAccountsPanelLifecycleView
        {
            Visible = true,
            ParentFormVisible = true
        };
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);
        coordinator.Initialize(view);
        coordinator.Initialize();
        view.ResetCounters();
        processRefreshController.Reset();
        timerCoordinator.Reset();

        coordinator.OnVisibleChanged(true);
        await view.AwaitRefreshesAsync(1);

        Assert.Equal([true], timerCoordinator.VisibilityChanges);
        Assert.Equal([true], processRefreshController.VisibilityChanges);
        Assert.Equal(1, view.RefreshGridCallCount);
        Assert.Equal(1, processRefreshController.TriggerImmediateRefreshCallCount);

        view.ResetCounters();
        processRefreshController.Reset();
        timerCoordinator.Reset();
        view.IsRefreshing = true;
        coordinator.OnVisibleChanged(true);
        await Task.Yield();
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);

        view.IsRefreshing = false;
        view.IsOperationInProgress = true;
        coordinator.OnVisibleChanged(true);
        await Task.Yield();
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);

        view.IsOperationInProgress = false;
        view.IsSortActive = true;
        coordinator.OnVisibleChanged(true);
        await Task.Yield();
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);

        view.IsSortActive = false;
        view.ParentFormVisible = false;
        coordinator.OnVisibleChanged(true);
        await Task.Yield();
        Assert.Equal([true, true, true, false], timerCoordinator.VisibilityChanges);
        Assert.Equal([true, true, true, false], processRefreshController.VisibilityChanges);
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);

        view.ResetCounters();
        processRefreshController.Reset();
        timerCoordinator.Reset();
        view.ParentFormVisible = true;
        coordinator.OnVisibleChanged(false);
        await Task.Yield();
        Assert.Equal([false], timerCoordinator.VisibilityChanges);
        Assert.Equal([false], processRefreshController.VisibilityChanges);
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);
    }

    [Fact]
    public async Task AccountsPanelLifecycleCoordinator_TimerRefreshEvent_RefreshesOnlyWhenIdle()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var view = new FakeAccountsPanelLifecycleView();
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);
        coordinator.Initialize(view);

        timerCoordinator.RaiseRefreshNeeded();
        await view.AwaitRefreshesAsync(1);
        Assert.Equal(1, view.RefreshGridCallCount);

        view.ResetCounters();
        view.IsRefreshing = true;
        timerCoordinator.RaiseRefreshNeeded();
        await Task.Yield();
        Assert.Equal(0, view.RefreshGridCallCount);

        view.IsRefreshing = false;
        view.IsOperationInProgress = true;
        timerCoordinator.RaiseRefreshNeeded();
        await Task.Yield();
        Assert.Equal(0, view.RefreshGridCallCount);
    }

    [Fact]
    public async Task AccountsPanelLifecycleCoordinator_RefreshOnActivationAsync_HonorsLoadedAndSortGates()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var view = new FakeAccountsPanelLifecycleView();
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);
        coordinator.Initialize(view);

        await coordinator.RefreshOnActivationAsync();
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);

        coordinator.Initialize();
        view.ResetCounters();
        processRefreshController.Reset();

        await coordinator.RefreshOnActivationAsync();
        Assert.Equal(1, view.RefreshGridCallCount);
        Assert.Equal(1, processRefreshController.TriggerImmediateRefreshCallCount);

        view.ResetCounters();
        processRefreshController.Reset();
        view.IsSortActive = true;
        await coordinator.RefreshOnActivationAsync();
        Assert.Equal(0, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);

        view.IsSortActive = false;
        view.RefreshImplementation = _ =>
        {
            view.IsSortActive = true;
            return Task.CompletedTask;
        };
        await coordinator.RefreshOnActivationAsync();
        Assert.Equal(1, view.RefreshGridCallCount);
        Assert.Equal(0, processRefreshController.TriggerImmediateRefreshCallCount);
    }

    [Fact]
    public void AccountsPanelLifecycleCoordinator_SidChangeDetected_ForwardsWarningToView()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var view = new FakeAccountsPanelLifecycleView();
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);
        coordinator.Initialize(view);

        timerCoordinator.RaiseSidChangeDetected();

        Assert.Equal(1, view.ShowSidMigrationWarningCallCount);
    }

    [Fact]
    public void AccountsPanelLifecycleCoordinator_StopProcessRefresh_StopsTimerAndHidesProcessRefresh()
    {
        var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
        var processRefreshController = new FakeAccountsPanelProcessRefreshController();
        var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);

        coordinator.StopProcessRefresh();

        Assert.Equal(1, timerCoordinator.StopCallCount);
        Assert.Equal([false], processRefreshController.VisibilityChanges);
    }

    [Fact]
    public void AccountsPanelLifecycleCoordinator_ParentResize_ForwardsMinimizedState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var timerCoordinator = new FakeAccountsPanelTimerCoordinator();
            var processRefreshController = new FakeAccountsPanelProcessRefreshController();
            var coordinator = new AccountsPanelLifecycleCoordinator(timerCoordinator, processRefreshController);
            using var form = new TestForm();

            coordinator.OnParentChanged(form);

            form.WindowState = FormWindowState.Normal;
            form.RaiseResize();
            form.WindowState = FormWindowState.Minimized;
            form.RaiseResize();

            Assert.Equal([false, true], processRefreshController.ParentResizeStates);
        });
    }

    [Fact]
    public async Task AccountsPanelSelectionSaveCoordinator_SaveRefreshSelectAndDataChanged_RunInOrder()
    {
        using var state = new SelectionSaveTestState();
        var coordinator = state.CreateCoordinator();

        await coordinator.SaveRefreshAndSelectAsync("S-1-5-21-target", CancellationToken.None);

        Assert.Equal(
        [
            "ui-invoke",
            "save",
            "refresh",
            "select:S-1-5-21-target",
            "data-changed"
        ], state.Events);
        Assert.Same(state.View.CredentialStore, state.SaveStore);
        Assert.Same(state.View.Database, state.SaveDatabase);
        Assert.Same(state.View.Session.PinDerivedKey, state.SavePinKey);
    }

    [Fact]
    public async Task AccountsPanelSelectionSaveCoordinator_CancellationBeforeSave_PreventsAllWork()
    {
        using var state = new SelectionSaveTestState();
        var coordinator = state.CreateCoordinator();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coordinator.SaveRefreshAndSelectAsync("S-1-5-21-target", cancellationSource.Token));

        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task AccountsPanelSelectionSaveCoordinator_CancellationAfterRefresh_PreventsSelectAndDataChanged()
    {
        using var state = new SelectionSaveTestState();
        state.RefreshCallback = cancellationToken => state.CancelAfterRefresh();
        var coordinator = state.CreateCoordinator();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coordinator.SaveRefreshAndSelectAsync("S-1-5-21-target", state.CancellationSource.Token));

        Assert.Equal(
        [
            "ui-invoke",
            "save",
            "refresh"
        ], state.Events);
    }

    [Fact]
    public void GroupHeaderRow_UpdatesBoldFont_WhenGridFontChanges()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var iconLifetimeManager = new AccountGridIconLifetimeManager();
            using var grid = new DataGridView
            {
                Columns =
                {
                    new DataGridViewTextBoxColumn { Name = "Import" },
                    new DataGridViewImageColumn { Name = "Credential" },
                    new DataGridViewTextBoxColumn { Name = "Account" },
                    new DataGridViewCheckBoxColumn { Name = "Logon" },
                    new DataGridViewCheckBoxColumn { Name = "AllowInternet" },
                    new DataGridViewTextBoxColumn { Name = "Apps" },
                    new DataGridViewTextBoxColumn { Name = "ProfilePath" },
                    new DataGridViewTextBoxColumn { Name = "SID" },
                }
            };
            host.Controls.Add(grid);
            StaTestHelper.CreateControlTree(host);

            using var composer = new AccountGridRowComposer(iconLifetimeManager);
            var headerRow = composer.AddGroupHeaderRow(grid, "Users");

            var originalFont = headerRow.DefaultCellStyle.Font!;
            using var updatedGridFont = new Font(originalFont.FontFamily, originalFont.Size + 1, FontStyle.Regular);
            grid.Font = updatedGridFont;
            StaTestHelper.PumpUntil(() => !ReferenceEquals(headerRow.DefaultCellStyle.Font, originalFont));

            var rowFont = headerRow.DefaultCellStyle.Font!;
            Assert.NotEqual(originalFont, rowFont);
            Assert.Equal(updatedGridFont.FontFamily.Name, rowFont.FontFamily.Name);
            Assert.Equal(updatedGridFont.Size, rowFont.Size, precision: 1);
            Assert.True(rowFont.Bold);

            using (var rowFontClone = new Font(rowFont, rowFont.Style))
            {
                Assert.NotNull(rowFontClone);
            }
        });
    }

    private sealed class FakeAccountsPanelLifecycleView : IAccountsPanelLifecycleView
    {
        private TaskCompletionSource _refreshObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Visible { get; set; }
        public bool IsRefreshing { get; set; }
        public bool IsOperationInProgress { get; set; }
        public bool IsSortActive { get; set; }
        public bool ParentFormVisible { get; set; } = true;
        public int InitialRefreshCallCount { get; private set; }
        public int RefreshGridCallCount { get; private set; }
        public int ShowSidMigrationWarningCallCount { get; private set; }
        public Func<CancellationToken, Task>? RefreshImplementation { get; set; }

        public bool IsParentFormVisible() => ParentFormVisible;

        public Task InitialRefreshAsync()
        {
            InitialRefreshCallCount++;
            return Task.CompletedTask;
        }

        public Task RefreshGridAsync(CancellationToken cancellationToken = default)
        {
            RefreshGridCallCount++;
            var task = RefreshImplementation?.Invoke(cancellationToken) ?? Task.CompletedTask;
            _refreshObserved.TrySetResult();
            return task;
        }

        public void ShowSidMigrationWarning()
            => ShowSidMigrationWarningCallCount++;

        public async Task AwaitRefreshesAsync(int expectedCount)
        {
            if (RefreshGridCallCount >= expectedCount)
                return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _refreshObserved.Task.WaitAsync(timeout.Token);
            Assert.True(RefreshGridCallCount >= expectedCount);
        }

        public void ResetCounters()
        {
            RefreshGridCallCount = 0;
            _refreshObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class FakeAccountsPanelTimerCoordinator : IAccountsPanelTimerCoordinator
    {
        public event Action? SidChangeDetected;
        public event Action? RefreshNeeded;

        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public List<bool> VisibilityChanges { get; } = [];

        public void Start() => StartCallCount++;

        public void Stop() => StopCallCount++;

        public void NotifyVisibilityChanged(bool isVisible)
            => VisibilityChanges.Add(isVisible);

        public void RaiseSidChangeDetected()
            => SidChangeDetected?.Invoke();

        public void RaiseRefreshNeeded()
            => RefreshNeeded?.Invoke();

        public void Reset()
            => VisibilityChanges.Clear();
    }

    private sealed class FakeAccountsPanelProcessRefreshController : IAccountsPanelProcessRefreshController
    {
        public int StartCallCount { get; private set; }
        public int TriggerImmediateRefreshCallCount { get; private set; }
        public Func<bool>? VisibilityProbe { get; private set; }
        public List<bool> VisibilityChanges { get; } = [];
        public List<bool> ParentResizeStates { get; } = [];

        public void Start(Func<bool> isVisibleAndParentVisible)
        {
            StartCallCount++;
            VisibilityProbe = isVisibleAndParentVisible;
        }

        public void NotifyParentResized(bool isMinimized)
            => ParentResizeStates.Add(isMinimized);

        public void NotifyVisibilityChanged(bool isVisible)
            => VisibilityChanges.Add(isVisible);

        public void TriggerImmediateRefresh()
            => TriggerImmediateRefreshCallCount++;

        public void Reset()
        {
            VisibilityChanges.Clear();
            TriggerImmediateRefreshCallCount = 0;
        }
    }

    private sealed class TestForm : Form
    {
        public void RaiseResize() => OnResize(EventArgs.Empty);
    }

    private sealed class SelectionSaveTestState : IDisposable
    {
        private readonly SecureSecret _pinKey = TestSecretFactory.Create(32, 0x5A);
        private readonly FakeUiThreadInvoker _uiThreadInvoker;
        private readonly SessionPersistenceHelper _persistenceHelper;

        public SelectionSaveTestState()
        {
            _uiThreadInvoker = new FakeUiThreadInvoker(Events);
            View = new FakeAccountsPanelSelectionSaveView(Events, CreateSession(_pinKey));
            _persistenceHelper = new SessionPersistenceHelper(
                new FakeConfigReencryptionPersistence(this),
                new FakeMainConfigPersistence(),
                new FakeSidNameCacheService(),
                () => _uiThreadInvoker,
                new FakeLoggingService());
        }

        public List<string> Events { get; } = [];
        public FakeAccountsPanelSelectionSaveView View { get; }
        public CredentialStore? SaveStore { get; private set; }
        public AppDatabase? SaveDatabase { get; private set; }
        public ISecureSecretSnapshotSource? SavePinKey { get; private set; }
        public CancellationTokenSource CancellationSource { get; } = new();
        public Action<CancellationToken>? RefreshCallback { get; set; }

        public AccountsPanelSelectionSaveCoordinator CreateCoordinator()
        {
            var coordinator = new AccountsPanelSelectionSaveCoordinator(_persistenceHelper);
            coordinator.Initialize(View);
            View.RefreshImplementation = cancellationToken =>
            {
                Events.Add("refresh");
                RefreshCallback?.Invoke(cancellationToken);
                return Task.CompletedTask;
            };
            return coordinator;
        }

        public void RecordSave(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource pinKey)
        {
            Events.Add("save");
            SaveStore = store;
            SaveDatabase = database;
            SavePinKey = pinKey;
        }

        public void CancelAfterRefresh()
            => CancellationSource.Cancel();

        public void Dispose()
        {
            CancellationSource.Dispose();
            View.Dispose();
            _pinKey.Dispose();
        }

        private static SessionContext CreateSession(SecureSecret pinKey)
            => new SessionContext
            {
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore()
            }.WithClonedPinDerivedKey(pinKey);
    }

    private sealed class FakeAccountsPanelSelectionSaveView(List<string> events, SessionContext session)
        : IAccountsPanelSelectionSaveView, IDisposable
    {
        public AppDatabase Database => Session.Database;
        public CredentialStore CredentialStore => Session.CredentialStore;
        public SessionContext Session { get; } = session;
        public Func<CancellationToken, Task> RefreshImplementation { get; set; } = _ => Task.CompletedTask;

        public Task RefreshGridAsync(CancellationToken cancellationToken = default)
            => RefreshImplementation(cancellationToken);

        public void SelectBySid(string sid)
            => events.Add($"select:{sid}");

        public void RaiseDataChanged()
            => events.Add("data-changed");

        public void Dispose()
            => Session.Dispose();
    }

    private sealed class FakeUiThreadInvoker(List<string> events) : IUiThreadInvoker
    {
        public T Invoke<T>(Func<T> func)
        {
            events.Add("ui-invoke");
            return func();
        }

        public void BeginInvoke(Action action)
            => action();
    }

    private sealed class FakeConfigReencryptionPersistence(SelectionSaveTestState state) : IConfigReencryptionPersistence
    {
        public void SaveCredentialStoreAndConfig(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey)
            => state.RecordSave(store, database, pinDerivedKey);

        public void SaveCredentialStoreAndAllConfigs(
            CredentialStore store,
            AppDatabase database,
            ISecureSecretSnapshotSource pinDerivedKey,
            List<(string path, AppConfig config)> additionalConfigs)
            => throw new NotSupportedException();
    }

    private sealed class FakeMainConfigPersistence : IMainConfigPersistence
    {
        public AppDatabase LoadConfig(ISecureSecretSnapshotSource pinDerivedKey)
            => throw new NotSupportedException();

        public AppDatabase LoadConfigFromPath(string configPath, ISecureSecretSnapshotSource pinDerivedKey)
            => throw new NotSupportedException();

        public void SaveConfig(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt)
            => throw new NotSupportedException();
    }

    private sealed class FakeSidNameCacheService : ISidNameCacheService
    {
        public string GetDisplayName(string sid) => sid;

        public string ResolveAndCache(string sid, string? fallbackName = null)
            => fallbackName ?? sid;

        public void UpdateName(string sid, string name)
        {
        }
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public string LogFilePath => string.Empty;
        public bool Enabled { get; set; }
        public LogVerbosity Verbosity { get; set; }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void Fatal(string message, Exception? ex = null) { }
    }
}

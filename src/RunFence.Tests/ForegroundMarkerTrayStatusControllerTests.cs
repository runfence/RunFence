using System.Drawing;
using Moq;
using RunFence.Account;
using RunFence.ForegroundMarker;
using RunFence.TrayIcon;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class ForegroundMarkerTrayStatusControllerTests
{
    [Fact]
    public void Initialize_AppliesPreExistingActiveStateToTooltipAndOverlay()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource
        {
            CurrentState = CreateActiveState(ForegroundPrivilegeMarkerKind.Isolated, Color.LimeGreen, "chrome.exe", "S-1")
        };
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("BrowserUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();

        controller.Initialize(form);

        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", "[Isolated]"),
            notifyIcon.Text);
        Assert.Equal(Color.LimeGreen.ToArgb(), overlaySink.LastColor?.ToArgb());
    }

    [Fact]
    public void StateChanged_ResolvesSidNameOnlyInsideMarshaledUiThreadPath()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>(MockBehavior.Strict);
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("BrowserUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm(executeImmediately: false);
        controller.Initialize(form);

        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1"));

        sidNameCache.Verify(x => x.GetDisplayName(It.IsAny<string>()), Times.Never);
        Assert.Equal(captionTextBuilder.BuildBaseTrayTooltip(), notifyIcon.Text);

        form.ExecutePendingActions();

        sidNameCache.Verify(x => x.GetDisplayName("S-1"), Times.Once);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", null),
            notifyIcon.Text);
        Assert.Equal(Color.Blue.ToArgb(), overlaySink.LastColor?.ToArgb());
    }

    [Fact]
    public void RepeatedSid_ReusesCachedDisplayNameUntilUpdateTrayClearsCache()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.SetupSequence(x => x.GetDisplayName("S-1"))
            .Returns("BrowserUser")
            .Returns("RenamedUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();
        controller.Initialize(form);

        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1"));
        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1"));

        sidNameCache.Verify(x => x.GetDisplayName("S-1"), Times.Once);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", null),
            notifyIcon.Text);

        controller.UpdateTray();

        sidNameCache.Verify(x => x.GetDisplayName("S-1"), Times.Exactly(2));
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "RenamedUser", null),
            notifyIcon.Text);
    }

    [Fact]
    public void NullAccountSid_UsesUnknownAccountWithoutSidLookup()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>(MockBehavior.Strict);
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();
        controller.Initialize(form);

        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.LowIL, Color.OrangeRed, "cmd.exe", null));

        sidNameCache.Verify(x => x.GetDisplayName(It.IsAny<string>()), Times.Never);
        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("cmd.exe", "Unknown account", "[LowIL]"),
            notifyIcon.Text);
        Assert.Equal(Color.OrangeRed.ToArgb(), overlaySink.LastColor?.ToArgb());
    }

    [Fact]
    public void HiddenCurrentAccountState_UsesTooltipWithoutOverlay()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        const string currentSid = "S-1-current";
        var stateSource = new TestMarkerStateSource
        {
            CurrentState = ForegroundPrivilegeMarkerState.TooltipOnly(
                new ForegroundPrivilegeMarkerMetadata("powershell.exe", currentSid))
        };
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName(currentSid)).Returns("AdminUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();

        controller.Initialize(form);

        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("powershell.exe", "AdminUser", null),
            notifyIcon.Text);
        Assert.Null(overlaySink.LastColor);
    }

    [Fact]
    public void HiddenHighIlState_UsesHighIlTooltipWithoutOverlay()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource
        {
            CurrentState = ForegroundPrivilegeMarkerState.TooltipOnly(
                new ForegroundPrivilegeMarkerMetadata("powershell.exe", "S-1"),
                tooltipMode: ForegroundPrivilegeTooltipMode.HighIL)
        };
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("AdminUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();

        controller.Initialize(form);

        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("powershell.exe", "AdminUser", "[HighIL]"),
            notifyIcon.Text);
        Assert.Null(overlaySink.LastColor);
    }

    [Fact]
    public void HiddenElevatedState_UsesElevatedTooltipWithoutOverlay()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource
        {
            CurrentState = ForegroundPrivilegeMarkerState.TooltipOnly(
                new ForegroundPrivilegeMarkerMetadata("powershell.exe", "S-1"),
                tooltipMode: ForegroundPrivilegeTooltipMode.Elevated)
        };
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("AdminUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();

        controller.Initialize(form);

        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("powershell.exe", "AdminUser", "[Elevated]"),
            notifyIcon.Text);
        Assert.Null(overlaySink.LastColor);
    }

    [Fact]
    public void InactiveState_ClearsForegroundSuffixAndOverlay()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("BrowserUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();
        controller.Initialize(form);
        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1"));

        stateSource.RaiseStateChanged(ForegroundPrivilegeMarkerState.Inactive);

        Assert.Equal(captionTextBuilder.BuildBaseTrayTooltip(), notifyIcon.Text);
        Assert.Null(overlaySink.LastColor);
    }

    [Fact]
    public void UpdateTrayTooltip_RebuildsLicenseFreeBaseWhilePreservingActiveSuffix()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("BrowserUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm();
        controller.Initialize(form);
        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.Isolated, Color.Green, "chrome.exe", "S-1"));
        notifyIcon.Text = "RunFence (Evaluation)";

        controller.UpdateTrayTooltip();

        Assert.Equal(
            captionTextBuilder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", "[Isolated]"),
            notifyIcon.Text);
    }

    [Fact]
    public void Dispose_IgnoresQueuedUpdatesAfterDisposal()
    {
        using var notifyIcon = new NotifyIcon();
        var captionTextBuilder = new ApplicationCaptionTextBuilder();
        var overlaySink = new RecordingOverlaySink();
        var stateSource = new TestMarkerStateSource();
        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(x => x.GetDisplayName("S-1")).Returns("BrowserUser");
        var controller = new ForegroundMarkerTrayStatusController(
            notifyIcon,
            overlaySink,
            stateSource,
            sidNameCache.Object,
            captionTextBuilder);
        using var form = new QueueingTestForm(executeImmediately: false);
        controller.Initialize(form);

        stateSource.RaiseStateChanged(CreateActiveState(ForegroundPrivilegeMarkerKind.Basic, Color.Blue, "chrome.exe", "S-1"));
        controller.Dispose();
        form.ExecutePendingActions();

        Assert.Equal(captionTextBuilder.BuildBaseTrayTooltip(), notifyIcon.Text);
        Assert.Null(overlaySink.LastColor);
    }

    private static ForegroundPrivilegeMarkerState CreateActiveState(
        ForegroundPrivilegeMarkerKind kind,
        Color color,
        string processName,
        string? accountSid)
    {
        return ForegroundPrivilegeMarkerState.Active(
            kind,
            color,
            new ForegroundPrivilegeMarkerMetadata(processName, accountSid));
    }

    private sealed class RecordingOverlaySink : ITrayForegroundMarkerOverlaySink
    {
        public Color? LastColor { get; private set; }

        public void SetForegroundMarkerOverlay(Color? color) => LastColor = color;
    }

    private sealed class TestMarkerStateSource : IForegroundPrivilegeMarkerStateSource
    {
        public event Action<ForegroundPrivilegeMarkerState>? StateChanged;

        public ForegroundPrivilegeMarkerState CurrentState { get; set; } = ForegroundPrivilegeMarkerState.Inactive;

        public void RaiseStateChanged(ForegroundPrivilegeMarkerState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(state);
        }
    }

    private sealed class QueueingTestForm : Control, IMainFormVisibility
    {
        private readonly bool _executeImmediately;
        private readonly Queue<Action> _pendingActions = new();
        private FormWindowState _windowState;
        private bool _showInTaskbar;

        public QueueingTestForm(bool executeImmediately = true)
        {
            _executeImmediately = executeImmediately;
            CreateControl();
        }

        public bool IsModalActive => false;
        public bool HasOtherWindowsOpen => false;

        public void BeginInvokeOnUiThread(Action action)
        {
            if (_executeImmediately)
            {
                action();
                return;
            }

            _pendingActions.Enqueue(action);
        }

        public void InvokeOnUiThread(Action action) => action();

        public void ExecutePendingActions()
        {
            while (_pendingActions.Count > 0)
                _pendingActions.Dequeue()();
        }

        string IMainFormVisibility.Title
        {
            set
            {
            }
        }

        FormWindowState IMainFormVisibility.WindowState
        {
            get => _windowState;
            set => _windowState = value;
        }

        bool IMainFormVisibility.ShowInTaskbar
        {
            get => _showInTaskbar;
            set => _showInTaskbar = value;
        }
    }
}

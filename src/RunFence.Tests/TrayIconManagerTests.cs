using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.TrayIcon;
using Xunit;

namespace RunFence.Tests;

public class TrayIconManagerTests
{
    [Fact]
    public void RebuildContextMenu_InsertsLockBetweenShowAndBlockInputInjection()
    {
        using var context = CreateManagerContext();

        var menu = context.NotifyIcon.ContextMenuStrip!;
        Assert.Equal("Show", menu.Items[0].Text);
        Assert.Equal("Lock", menu.Items[1].Text);
        Assert.Equal("Block Input Injection", menu.Items[2].Text);
    }

    [Fact]
    public void RebuildContextMenu_LockItemHasImage()
    {
        using var context = CreateManagerContext();

        var lockItem = context.NotifyIcon.ContextMenuStrip!.Items[1] as ToolStripMenuItem;
        Assert.NotNull(lockItem);
        Assert.NotNull(lockItem!.Image);
    }

    [Fact]
    public void RebuildContextMenu_HidesLockWhenUnlicensed()
    {
        using var context = CreateManagerContext(lockVisible: false);

        var lockItem = context.NotifyIcon.ContextMenuStrip!.Items[1] as ToolStripMenuItem;
        Assert.NotNull(lockItem);
        Assert.False(lockItem!.Available);
    }

    [Fact]
    public void RebuildContextMenu_HidesLockWhenLocked()
    {
        using var context = CreateManagerContext(lockVisible: false, isLocked: true);

        var lockItem = context.NotifyIcon.ContextMenuStrip!.Items[1] as ToolStripMenuItem;
        Assert.NotNull(lockItem);
        Assert.False(lockItem!.Available);
    }

    [Fact]
    public void RebuildContextMenu_WhenLocked_RenamesShowToUnlock()
    {
        using var context = CreateManagerContext(lockVisible: false, isLocked: true);

        Assert.Equal("Unlock", context.NotifyIcon.ContextMenuStrip!.Items[0].Text);
    }

    [Fact]
    public void RebuildContextMenu_ShowsAndEnablesLockForLicensedUnlockedAndNoModalOrSecondaryForms()
    {
        using var context = CreateManagerContext(lockVisible: true, lockEnabled: true);

        var lockItem = context.NotifyIcon.ContextMenuStrip!.Items[1] as ToolStripMenuItem;
        Assert.NotNull(lockItem);
        Assert.True(lockItem!.Available);
        Assert.True(lockItem.Enabled);
    }

    [Fact]
    public void RebuildContextMenu_DisablesLockWhenModalOpen()
    {
        using var context = CreateManagerContext(lockVisible: true, lockEnabled: false);

        var lockItem = context.NotifyIcon.ContextMenuStrip!.Items[1] as ToolStripMenuItem;
        Assert.NotNull(lockItem);
        Assert.True(lockItem!.Available);
        Assert.False(lockItem.Enabled);
    }

    [Fact]
    public void RebuildContextMenu_DisablesLockWhenSecondaryFormVisible()
    {
        using var context = CreateManagerContext(lockVisible: true, lockEnabled: false);

        var lockItem = context.NotifyIcon.ContextMenuStrip!.Items[1] as ToolStripMenuItem;
        Assert.NotNull(lockItem);
        Assert.True(lockItem!.Available);
        Assert.False(lockItem.Enabled);
    }

    [Fact]
    public void LockMenuItemInvokesTrayOwnerManualLock()
    {
        var owner = new RecordingTrayOwner(isTrayLockVisible: true, isTrayLockEnabled: true, isLocked: false);
        using var context = CreateManagerContext(trayOwner: owner);

        var lockItem = (ToolStripMenuItem)context.NotifyIcon.ContextMenuStrip!.Items[1];
        lockItem.PerformClick();

        Assert.True(owner.LockToTrayImmediatelyInvoked);
    }

    [Fact]
    public void BlockInputInjection_ClickRaisesExistingSinkEvent()
    {
        using var context = CreateManagerContext();
        var raised = false;
        context.Manager.InputInjectionToggleRequested += () => raised = true;

        ((ToolStripMenuItem)context.NotifyIcon.ContextMenuStrip!.Items[2]).PerformClick();

        Assert.True(raised);
    }

    [Fact]
    public void RebuildContextMenu_RetainsAppIconBitmapAcrossRebuilds()
    {
        using var context = CreateManagerContext();

        var firstBitmap = Assert.IsType<Bitmap>(((ToolStripMenuItem)context.NotifyIcon.ContextMenuStrip!.Items[0]).Image);
        context.Manager.UpdateDatabase(new CredentialStore());
        var secondBitmap = Assert.IsType<Bitmap>(((ToolStripMenuItem)context.NotifyIcon.ContextMenuStrip!.Items[0]).Image);

        Assert.Same(firstBitmap, secondBitmap);
        Assert.Equal(firstBitmap.Width, secondBitmap.Width);
    }

    [Fact]
    public void UpdateDatabase_BeforeInitialize_DefersMenuBuildUntilInitialized()
    {
        var notifyIcon = new NotifyIcon();
        using var _icon = notifyIcon;
        var mockAppIconProvider = new Mock<IAppIconProvider>();
        mockAppIconProvider.Setup(p => p.GetAppIcon()).Returns(SystemIcons.Application);
        var manager = new TrayIconManager(
            notifyIcon,
            mockAppIconProvider.Object,
            Mock.Of<IDatabaseProvider>(x => x.GetDatabase() == new AppDatabase()),
            new TrayMenuBuilder(
                new SidDisplayNameResolver(new Mock<ISidResolver>().Object, new Mock<IProfilePathResolver>().Object),
                new Mock<IIconService>().Object,
                new TrayMenuDiscoveryBuilder(new Mock<IShortcutIconHelper>().Object)),
            new Mock<IInputInjectionBlockerService>().Object);
        using var _ = manager;

        manager.UpdateDatabase(new CredentialStore());

        Assert.Null(notifyIcon.ContextMenuStrip);

        var owner = new RecordingTrayOwner(true, true, false);
        manager.Initialize(owner, new RecordingActionHandler());

        Assert.NotNull(notifyIcon.ContextMenuStrip);
    }

    [Fact]
    public void UpdateDiscoveredApps_BeforeInitialize_DefersMenuBuildUntilInitialized()
    {
        var notifyIcon = new NotifyIcon();
        using var _icon = notifyIcon;
        var mockAppIconProvider = new Mock<IAppIconProvider>();
        mockAppIconProvider.Setup(p => p.GetAppIcon()).Returns(SystemIcons.Application);
        var manager = new TrayIconManager(
            notifyIcon,
            mockAppIconProvider.Object,
            Mock.Of<IDatabaseProvider>(x => x.GetDatabase() == new AppDatabase()),
            new TrayMenuBuilder(
                new SidDisplayNameResolver(new Mock<ISidResolver>().Object, new Mock<IProfilePathResolver>().Object),
                new Mock<IIconService>().Object,
                new TrayMenuDiscoveryBuilder(new Mock<IShortcutIconHelper>().Object)),
            new Mock<IInputInjectionBlockerService>().Object);
        using var _ = manager;

        manager.UpdateDiscoveredApps([new StartMenuEntry("Notepad", @"C:\Windows\Notepad.exe", "S-1-5-21-test", null)]);

        Assert.Null(notifyIcon.ContextMenuStrip);

        var owner = new RecordingTrayOwner(true, true, false);
        manager.Initialize(owner, new RecordingActionHandler());

        Assert.NotNull(notifyIcon.ContextMenuStrip);
    }

    private static ManagerContext CreateManagerContext(
        bool lockVisible = true,
        bool lockEnabled = true,
        bool isLocked = false,
        ITrayOwner? trayOwner = null)
    {
        var owner = trayOwner as RecordingTrayOwner
                    ?? new RecordingTrayOwner(lockVisible, lockEnabled, isLocked);

        var notifyIcon = new NotifyIcon();
        var mockAppIconProvider = new Mock<IAppIconProvider>();
        mockAppIconProvider.Setup(p => p.GetAppIcon()).Returns(SystemIcons.Application);
        var mockDbProvider = new Mock<IDatabaseProvider>();
        mockDbProvider.Setup(p => p.GetDatabase()).Returns(new AppDatabase());
        var mockSidResolver = new Mock<ISidResolver>();
        var mockProfilePathResolver = new Mock<IProfilePathResolver>();
        var mockInjectionBlocker = new Mock<IInputInjectionBlockerService>();
        var trayMenuBuilder = new TrayMenuBuilder(
            new SidDisplayNameResolver(mockSidResolver.Object, mockProfilePathResolver.Object),
            new Mock<IIconService>().Object,
            new TrayMenuDiscoveryBuilder(new Mock<IShortcutIconHelper>().Object));
        var actionHandler = new RecordingActionHandler();

        var manager = new TrayIconManager(
            notifyIcon,
            mockAppIconProvider.Object,
            mockDbProvider.Object,
            trayMenuBuilder,
            mockInjectionBlocker.Object)
        { };

        manager.Initialize(owner, actionHandler);

        return new ManagerContext(manager, notifyIcon, owner, actionHandler);
    }

    private sealed class RecordingTrayOwner : ITrayOwner
    {
        public bool IsTrayLockVisible { get; }
        public bool IsTrayLockEnabled { get; }
        public bool IsLocked { get; }
        public bool LockToTrayImmediatelyInvoked { get; private set; }

        public RecordingTrayOwner(bool isTrayLockVisible, bool isTrayLockEnabled, bool isLocked)
        {
            IsTrayLockVisible = isTrayLockVisible;
            IsTrayLockEnabled = isTrayLockEnabled;
            IsLocked = isLocked;
        }

        public Task TryShowWindowAsync() => Task.CompletedTask;

        public void LockToTrayImmediately() => LockToTrayImmediatelyInvoked = true;
    }

    private sealed class RecordingActionHandler : ITrayMenuActionHandler
    {
        public void LaunchConfiguredApp(AppEntry app) { }
        public void LaunchFolderBrowser(string accountSid, bool shift) { }
        public void LaunchTerminal(string accountSid, bool shift) { }
        public void LaunchDiscoveredApp(string exePath, string accountSid) { }
        public void ExitApplication() { }
    }

    private sealed class ManagerContext(
        TrayIconManager manager,
        NotifyIcon notifyIcon,
        RecordingTrayOwner owner,
        RecordingActionHandler actionHandler)
        : IDisposable
    {
        public TrayIconManager Manager { get; } = manager;
        public NotifyIcon NotifyIcon { get; } = notifyIcon;
        public RecordingTrayOwner Owner { get; } = owner;
        public RecordingActionHandler ActionHandler { get; } = actionHandler;

        public void Dispose()
        {
            Manager.Dispose();
            NotifyIcon.Dispose();
        }
    }
}

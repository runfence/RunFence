using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.TrayIcon;
using System.Drawing;
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
    public void SetForegroundMarkerOverlay_AppliesBadgeAndRestoresBase()
    {
        using var baseIcon = CreateTestIcon(Color.SlateGray);
        using var context = CreateManagerContext(baseIcon: baseIcon);
        var badgeCenter = GetBadgeCenter(baseIcon);
        var baseColor = GetPixel(baseIcon, badgeCenter);

        context.Manager.SetForegroundMarkerOverlay(Color.Red);
        using (var markerBitmap = new Bitmap(context.NotifyIcon.Icon!.ToBitmap()))
        {
            var markerColor = markerBitmap.GetPixel(badgeCenter.X, badgeCenter.Y);
            Assert.NotEqual(baseColor, markerColor);
            Assert.Equal(Color.Red.ToArgb(), markerColor.ToArgb());
        }

        context.Manager.SetForegroundMarkerOverlay(null);
        using var restoredBitmap = new Bitmap(context.NotifyIcon.Icon.ToBitmap());
        var restoredColor = restoredBitmap.GetPixel(badgeCenter.X, badgeCenter.Y);
        Assert.Equal(baseColor, restoredColor);
    }

    [Fact]
    public void SetForegroundMarkerOverlay_IsIdempotentForSameColor()
    {
        using var context = CreateManagerContext(baseIcon: CreateTestIcon(Color.LightBlue));
        context.Manager.SetForegroundMarkerOverlay(Color.MediumSeaGreen);

        var firstHandle = context.NotifyIcon.Icon!.Handle;
        context.Manager.SetForegroundMarkerOverlay(Color.MediumSeaGreen);
        var secondHandle = context.NotifyIcon.Icon!.Handle;

        Assert.Equal(firstHandle, secondHandle);
    }

    [Fact]
    public void SetForegroundMarkerOverlay_ReplacesGeneratedOverlayOnColorChange()
    {
        using var baseIcon = CreateTestIcon(Color.Black);
        using var context = CreateManagerContext(baseIcon: baseIcon);
        var badgeCenter = GetBadgeCenter(baseIcon);

        context.Manager.SetForegroundMarkerOverlay(Color.Red);
        using (var redBitmap = new Bitmap(context.NotifyIcon.Icon!.ToBitmap()))
        {
            Assert.Equal(Color.Red.ToArgb(), redBitmap.GetPixel(badgeCenter.X, badgeCenter.Y).ToArgb());
        }

        context.Manager.SetForegroundMarkerOverlay(Color.Blue);
        using (var blueBitmap = new Bitmap(context.NotifyIcon.Icon!.ToBitmap()))
        {
            Assert.Equal(Color.Blue.ToArgb(), blueBitmap.GetPixel(badgeCenter.X, badgeCenter.Y).ToArgb());
        }
    }

    [Fact]
    public void Dispose_DoesNotDisposeProviderOwnedBaseIcon()
    {
        using var baseIcon = CreateTestIcon(Color.Gold);
        var context = CreateManagerContext(baseIcon: baseIcon);
        context.Manager.SetForegroundMarkerOverlay(Color.Red);

        context.Manager.Dispose();

        using var baseBitmap = new Bitmap(baseIcon.ToBitmap());
        Assert.NotNull(baseBitmap);
        context.Dispose();
    }

    [Fact]
    public void Dispose_WhenOverlayActive_IsIdempotentAndKeepsProviderBaseIconUsable()
    {
        using var baseIcon = CreateTestIcon(Color.MediumPurple);
        using var context = CreateManagerContext(baseIcon: baseIcon);
        context.Manager.SetForegroundMarkerOverlay(Color.OrangeRed);

        context.Manager.Dispose();
        context.Manager.Dispose();

        using var baseBitmap = new Bitmap(baseIcon.ToBitmap());
        Assert.NotNull(baseBitmap);
    }

    [Fact]
    public void RestoreIconVisibility_RestoresActiveOverlayOrBase()
    {
        using var baseIcon = CreateTestIcon(Color.CornflowerBlue);
        using var context = CreateManagerContext(baseIcon: baseIcon);
        var badgeCenter = GetBadgeCenter(baseIcon);
        var baseColor = GetPixel(baseIcon, badgeCenter);

        context.Manager.SetForegroundMarkerOverlay(Color.DarkOrange);
        context.Manager.RestoreIconVisibility();
        using (var overlayBitmap = new Bitmap(context.NotifyIcon.Icon!.ToBitmap()))
        {
            Assert.NotEqual(baseColor, overlayBitmap.GetPixel(badgeCenter.X, badgeCenter.Y));
        }

        context.Manager.SetForegroundMarkerOverlay(null);
        context.Manager.RestoreIconVisibility();
        using (var restoredBitmap = new Bitmap(context.NotifyIcon.Icon!.ToBitmap()))
        {
            Assert.Equal(baseColor, restoredBitmap.GetPixel(badgeCenter.X, badgeCenter.Y));
        }
    }

    [Fact]
    public void Dispose_RevertsOverlayToBaseBeforeDisposingGeneratedIcon()
    {
        using var baseIcon = CreateTestIcon(Color.DarkSeaGreen);
        using var context = CreateManagerContext(baseIcon: baseIcon);
        var badgeCenter = GetBadgeCenter(baseIcon);
        var baseColor = GetPixel(baseIcon, badgeCenter);

        context.Manager.SetForegroundMarkerOverlay(Color.Crimson);

        context.Manager.Dispose();
        using var finalBitmap = new Bitmap(context.NotifyIcon.Icon!.ToBitmap());
        Assert.Equal(baseColor, finalBitmap.GetPixel(badgeCenter.X, badgeCenter.Y));
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
            new Mock<IInputInjectionBlockerService>().Object,
            new TrayIconOverlayRenderer());
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
            new Mock<IInputInjectionBlockerService>().Object,
            new TrayIconOverlayRenderer());
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
        ITrayOwner? trayOwner = null,
        Icon? baseIcon = null)
    {
        var owner = trayOwner as RecordingTrayOwner
                    ?? new RecordingTrayOwner(lockVisible, lockEnabled, isLocked);

        var notifyIcon = new NotifyIcon();
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
        var resolvedBaseIcon = baseIcon ?? SystemIcons.Application;
        var mockAppIconProvider = new Mock<IAppIconProvider>();
        mockAppIconProvider.Setup(p => p.GetAppIcon()).Returns(resolvedBaseIcon);

        var manager = new TrayIconManager(
            notifyIcon,
            mockAppIconProvider.Object,
            mockDbProvider.Object,
            trayMenuBuilder,
            mockInjectionBlocker.Object,
            new TrayIconOverlayRenderer())
        { };

        manager.Initialize(owner, actionHandler);

        return new ManagerContext(manager, notifyIcon, owner, actionHandler);
    }

    private static Color GetPixel(Icon icon, Point point)
    {
        using var bitmap = new Bitmap(icon.ToBitmap());
        return bitmap.GetPixel(point.X, point.Y);
    }

    private static Point GetBadgeCenter(Icon icon)
    {
        var bounds = TrayIconOverlayRenderer.CalculateBadgeBounds(icon.Size);
        return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }

    private static Icon CreateTestIcon(Color fillColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(fillColor);
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            TrayIconOverlayNative.DestroyIcon(hIcon);
        }
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

using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.TrayIcon;
using Xunit;

namespace RunFence.Tests;

public class TrayMenuBuilderTests
{
    [Fact]
    public void BuildContextMenu_CreatesShowLockLaunchAndExitSections()
    {
        var builderContext = CreateBuilderContext();
        var builder = builderContext.Builder;
        var actionHandler = new RecordingTrayMenuActionHandler();
        using var appIcon = new Bitmap(16, 16);
        using var configuredIcon = new Bitmap(16, 16);
        using var discoveredIcon = new Bitmap(16, 16);
        var iconService = builderContext.IconService;
        iconService.Setup(x => x.GetOriginalAppIcon(It.IsAny<AppEntry>())).Returns((Image)configuredIcon.Clone());
        var shortcutHelper = builderContext.ShortcutHelper;
        shortcutHelper.Setup(x => x.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((Image?)null);

        var database = new AppDatabase();
        database.SidNames["S-1-5-21-folder"] = @"PC\FolderUser";
        database.SidNames["S-1-5-21-terminal"] = @"PC\TerminalUser";
        database.SidNames["S-1-5-21-app"] = @"PC\AppUser";
        database.Apps.Add(new AppEntry
        {
            Name = "Paint",
            AccountSid = "S-1-5-21-app",
            ExePath = @"C:\Apps\Paint.exe"
        });
        database.Accounts.Add(new AccountEntry { Sid = "S-1-5-21-folder", TrayFolderBrowser = true });
        database.Accounts.Add(new AccountEntry { Sid = "S-1-5-21-terminal", TrayTerminal = true });

        var credentialStore = new CredentialStore
        {
            Credentials =
            [
                new CredentialEntry { Sid = "S-1-5-21-folder", EncryptedPassword = [1] },
                new CredentialEntry { Sid = "S-1-5-21-terminal", EncryptedPassword = [1] }
            ]
        };
        var result = builder.BuildContextMenu(new TrayMenuBuildRequest(
            credentialStore,
            [new StartMenuEntry("Notepad", @"C:\Windows\Notepad.exe", "S-1-5-21-app", null)],
            new Dictionary<string, Image?>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\Windows\Notepad.exe"] = (Image)discoveredIcon.Clone()
            },
            database,
            appIcon,
            actionHandler));

        Assert.Equal("Show", result.ShowItem.Text);
        Assert.Equal("Lock", result.LockItem.Text);
        Assert.Contains(result.Menu.Items.OfType<ToolStripMenuItem>(), item => item.Text == "Paint");
        Assert.Contains(result.Menu.Items.OfType<ToolStripMenuItem>(), item => item.Text == "Notepad as AppUser");
        Assert.Contains(result.Menu.Items.OfType<ToolStripMenuItem>(), item => item.Text == "FolderUser");
        Assert.Contains(result.Menu.Items.OfType<ToolStripMenuItem>(), item => item.Text == "TerminalUser");
        Assert.Equal("Exit", result.Menu.Items[^1].Text);

        ((ToolStripMenuItem)result.Menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "Paint")).PerformClick();
        ((ToolStripMenuItem)result.Menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "FolderUser")).PerformClick();
        ((ToolStripMenuItem)result.Menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "TerminalUser")).PerformClick();
        ((ToolStripMenuItem)result.Menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "Notepad as AppUser")).PerformClick();
        ((ToolStripMenuItem)result.Menu.Items[^1]).PerformClick();

        Assert.Equal("Paint", actionHandler.LaunchedConfiguredApp?.Name);
        Assert.Equal(("S-1-5-21-folder", false), actionHandler.FolderBrowserLaunch);
        Assert.Equal(("S-1-5-21-terminal", false), actionHandler.TerminalLaunch);
        Assert.Equal((@"C:\Windows\Notepad.exe", "S-1-5-21-app"), actionHandler.DiscoveredAppLaunch);
        Assert.Equal(1, actionHandler.ExitCallCount);
    }

    [Fact]
    public void ApplyOwnerState_UpdatesTextAndWiresShowAndLockClicks()
    {
        var builder = CreateBuilderContext().Builder;
        using var appIcon = new Bitmap(16, 16);
        var result = builder.BuildContextMenu(new TrayMenuBuildRequest(
            null,
            null,
            new Dictionary<string, Image?>(StringComparer.OrdinalIgnoreCase),
            new AppDatabase(),
            appIcon,
            new RecordingTrayMenuActionHandler()));
        var owner = new RecordingTrayOwner(isLocked: true, isTrayLockVisible: true, isTrayLockEnabled: true);

        builder.ApplyOwnerState(owner, result.ShowItem, result.LockItem);
        result.ShowItem.PerformClick();
        result.LockItem.PerformClick();

        Assert.Equal("Unlock", result.ShowItem.Text);
        Assert.True(result.LockItem.Available);
        Assert.True(result.LockItem.Enabled);
        Assert.Equal(1, owner.ShowCallCount);
        Assert.Equal(1, owner.LockCallCount);
    }

    [Fact]
    public void ApplyOwnerState_RebindsActionsToLatestOwnerOnly()
    {
        var builder = CreateBuilderContext().Builder;
        using var appIcon = new Bitmap(16, 16);
        var result = builder.BuildContextMenu(new TrayMenuBuildRequest(
            null,
            null,
            new Dictionary<string, Image?>(StringComparer.OrdinalIgnoreCase),
            new AppDatabase(),
            appIcon,
            new RecordingTrayMenuActionHandler()));
        var firstOwner = new RecordingTrayOwner(
            isLocked: false,
            isTrayLockVisible: true,
            isTrayLockEnabled: true);
        var secondOwner = new RecordingTrayOwner(
            isLocked: true,
            isTrayLockVisible: true,
            isTrayLockEnabled: true);

        builder.ApplyOwnerState(firstOwner, result.ShowItem, result.LockItem);
        builder.ApplyOwnerState(secondOwner, result.ShowItem, result.LockItem);

        result.ShowItem.PerformClick();
        result.LockItem.PerformClick();

        Assert.Equal("Unlock", result.ShowItem.Text);
        Assert.Equal(0, firstOwner.ShowCallCount);
        Assert.Equal(0, firstOwner.LockCallCount);
        Assert.Equal(1, secondOwner.ShowCallCount);
        Assert.Equal(1, secondOwner.LockCallCount);
    }

    [Fact]
    public void DisposeMenuItemImages_ClearsNestedImages()
    {
        var builder = CreateBuilderContext().Builder;
        using var appIcon = new Bitmap(16, 16);
        using var nestedImage = new Bitmap(16, 16);
        var result = builder.BuildContextMenu(new TrayMenuBuildRequest(
            null,
            null,
            new Dictionary<string, Image?>(StringComparer.OrdinalIgnoreCase),
            new AppDatabase(),
            appIcon,
            new RecordingTrayMenuActionHandler()));
        var nested = new ToolStripMenuItem("Nested", (Image)nestedImage.Clone());
        result.LockItem.DropDownItems.Add(nested);

        builder.DisposeMenuItemImages(result.Menu.Items);

        Assert.Null(result.ShowItem.Image);
        Assert.Null(result.LockItem.Image);
        Assert.Null(nested.Image);
    }

    private static BuilderContext CreateBuilderContext()
    {
        var iconService = new Mock<IIconService>();
        var shortcutHelper = new Mock<IShortcutIconHelper>();
        var sidResolver = new Mock<ISidResolver>();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var builder = new TrayMenuBuilder(
            new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object),
            iconService.Object,
            new TrayMenuDiscoveryBuilder(shortcutHelper.Object));
        return new BuilderContext(builder, iconService, shortcutHelper);
    }

    private sealed class BuilderContext(
        TrayMenuBuilder builder,
        Mock<IIconService> iconService,
        Mock<IShortcutIconHelper> shortcutHelper)
    {
        public TrayMenuBuilder Builder { get; } = builder;
        public Mock<IIconService> IconService { get; } = iconService;
        public Mock<IShortcutIconHelper> ShortcutHelper { get; } = shortcutHelper;
    }

    private sealed class RecordingTrayMenuActionHandler : ITrayMenuActionHandler
    {
        public AppEntry? LaunchedConfiguredApp { get; private set; }
        public (string Sid, bool Shift)? FolderBrowserLaunch { get; private set; }
        public (string Sid, bool Shift)? TerminalLaunch { get; private set; }
        public (string ExePath, string Sid)? DiscoveredAppLaunch { get; private set; }
        public int ExitCallCount { get; private set; }

        public void LaunchConfiguredApp(AppEntry app) => LaunchedConfiguredApp = app;
        public void LaunchFolderBrowser(string accountSid, bool shift) => FolderBrowserLaunch = (accountSid, shift);
        public void LaunchTerminal(string accountSid, bool shift) => TerminalLaunch = (accountSid, shift);
        public void LaunchDiscoveredApp(string exePath, string accountSid) => DiscoveredAppLaunch = (exePath, accountSid);
        public void ExitApplication() => ExitCallCount++;
    }

    private sealed class RecordingTrayOwner(bool isLocked, bool isTrayLockVisible, bool isTrayLockEnabled) : ITrayOwner
    {
        public int ShowCallCount { get; private set; }
        public int LockCallCount { get; private set; }

        public Task TryShowWindowAsync()
        {
            ShowCallCount++;
            return Task.CompletedTask;
        }

        public void LockToTrayImmediately() => LockCallCount++;
        public bool IsLocked { get; } = isLocked;
        public bool IsTrayLockVisible { get; } = isTrayLockVisible;
        public bool IsTrayLockEnabled { get; } = isTrayLockEnabled;
    }
}

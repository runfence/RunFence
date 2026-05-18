using System.Drawing.Drawing2D;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.TrayIcon;

public class TrayMenuBuilder(
    SidDisplayNameResolver displayNameResolver,
    IIconService iconService,
    TrayMenuDiscoveryBuilder trayMenuDiscoveryBuilder)
{
    public TrayMenuBuildResult BuildContextMenu(TrayMenuBuildRequest request)
    {
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        var showItem = new ToolStripMenuItem("Show", request.AppIconBitmap);
        var lockItem = new ToolStripMenuItem("Lock", CreateLockIcon());

        menu.Items.Add(showItem);
        menu.Items.Add(lockItem);

        AddConfiguredApps(menu, request);
        AddDiscoveredApps(menu, request);
        AddAccountLaunchItems(
            menu,
            request,
            request.Database.Accounts.Where(a => a.TrayFolderBrowser).Select(a => a.Sid).ToList(),
            CreateFolderIcon,
            request.ActionHandler.LaunchFolderBrowser);
        AddAccountLaunchItems(
            menu,
            request,
            request.Database.Accounts.Where(a => a.TrayTerminal).Select(a => a.Sid).ToList(),
            CreateTerminalIcon,
            request.ActionHandler.LaunchTerminal);

        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("Exit", CreateExitIcon());
        exitItem.Click += (_, _) => request.ActionHandler.ExitApplication();
        menu.Items.Add(exitItem);

        return new TrayMenuBuildResult(menu, showItem, lockItem);
    }

    public void ApplyOwnerState(ITrayOwner owner, ToolStripMenuItem showItem, ToolStripMenuItem lockItem)
    {
        WireOwnerAction(showItem, owner, HandleShowClick);
        WireOwnerAction(lockItem, owner, HandleLockClick);

        showItem.Text = owner.IsLocked ? "Unlock" : "Show";
        lockItem.Visible = owner.IsTrayLockVisible;
        lockItem.Enabled = owner.IsTrayLockEnabled;
    }

    public void DisposeMenuItemImages(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item.Image != null)
            {
                item.Image.Dispose();
                item.Image = null;
            }

            if (item is ToolStripMenuItem { DropDownItems.Count: > 0 } menuItem)
                DisposeMenuItemImages(menuItem.DropDownItems);
        }
    }

    private void AddConfiguredApps(ContextMenuStrip menu, TrayMenuBuildRequest request)
    {
        if (request.Database.Apps.Count == 0)
            return;

        menu.Items.Add(new ToolStripSeparator());

        var nameGroups = request.Database.Apps
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var app in request.Database.Apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = app.Name;
            if (nameGroups.TryGetValue(app.Name, out int count) && count > 1)
            {
                var accountLabel = app.AppContainerName
                    ?? displayNameResolver.GetDisplayName(app.AccountSid, null, request.Database.SidNames);
                label = $"{app.Name} ({accountLabel})";
            }

            var item = new ToolStripMenuItem(label)
            {
                Image = iconService.GetOriginalAppIcon(app)
            };
            var capturedApp = app;
            item.Click += (_, _) => request.ActionHandler.LaunchConfiguredApp(capturedApp);
            menu.Items.Add(item);
        }
    }

    private void AddDiscoveredApps(ContextMenuStrip menu, TrayMenuBuildRequest request)
    {
        if (request.DiscoveredEntries?.Count is not > 0)
            return;

        menu.Items.Add(new ToolStripSeparator());
        var discoveredItems = trayMenuDiscoveryBuilder.BuildMenuItems(
            request.DiscoveredEntries.ToList(),
            request.Database.SidNames,
            request.IconCache,
            request.ActionHandler.LaunchDiscoveredApp,
            displayNameResolver);
        foreach (var item in discoveredItems)
            menu.Items.Add(item);
    }

    private void AddAccountLaunchItems(
        ContextMenuStrip menu,
        TrayMenuBuildRequest request,
        IReadOnlyList<string> traySids,
        Func<Image> iconFactory,
        Action<string, bool> onLaunch)
    {
        if (traySids.Count == 0)
            return;

        var credentialSids = request.CredentialStore?.Credentials
                                 .Select(c => c.Sid)
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase)
                             ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null)
            credentialSids.Add(interactiveSid);

        var sidsToShow = traySids
            .Where(sid => credentialSids.Contains(sid))
            .OrderBy(
                sid => displayNameResolver.ResolveUsername(sid, request.Database.SidNames)
                    ?? displayNameResolver.GetDisplayName(sid, null, request.Database.SidNames),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sidsToShow.Count == 0)
            return;

        menu.Items.Add(new ToolStripSeparator());
        foreach (var sid in sidsToShow)
        {
            var label = displayNameResolver.ResolveUsername(sid, request.Database.SidNames)
                ?? displayNameResolver.GetDisplayName(sid, null, request.Database.SidNames);
            var item = new ToolStripMenuItem(label, iconFactory());
            item.ToolTipText = "Hold Shift to launch with full privileges";
            var capturedSid = sid;
            item.Click += (_, _) => onLaunch(capturedSid, (Control.ModifierKeys & Keys.Shift) != 0);
            menu.Items.Add(item);
        }
    }

    private static void WireOwnerAction(
        ToolStripMenuItem menuItem,
        ITrayOwner owner,
        EventHandler clickHandler)
    {
        if (menuItem.Tag is not TrayOwnerMenuActionBinding binding)
        {
            menuItem.Tag = new TrayOwnerMenuActionBinding(owner);
            menuItem.Click += clickHandler;
            return;
        }

        binding.Owner = owner;
    }

    private static async void HandleShowClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: TrayOwnerMenuActionBinding binding })
            return;

        await binding.Owner.TryShowWindowAsync();
    }

    private static void HandleLockClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: TrayOwnerMenuActionBinding binding })
            return;

        binding.Owner.LockToTrayImmediately();
    }

    private static Image CreateLockIcon()
        => UiIconFactory.CreateToolbarIcon("\U0001F512", Color.FromArgb(0xA0, 0xA0, 0xA0), 16);

    private static Image CreateFolderIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(0xCC, 0x88, 0x22));
        g.FillRectangle(brush, 1, 5, 5, 2);
        g.FillRectangle(brush, 1, 6, 14, 8);
        return bmp;
    }

    private static Image CreateTerminalIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bgBrush = new SolidBrush(Color.FromArgb(0x1E, 0x1E, 0x1E));
        g.FillRectangle(bgBrush, 1, 2, 14, 12);
        using var promptPen = new Pen(Color.FromArgb(0x33, 0xDD, 0x66), 1.5f);
        promptPen.StartCap = LineCap.Round;
        promptPen.EndCap = LineCap.Round;
        g.DrawLine(promptPen, 3, 7, 6, 9);
        g.DrawLine(promptPen, 6, 9, 3, 11);
        using var cursorBrush = new SolidBrush(Color.FromArgb(0xCC, 0xCC, 0xCC));
        g.FillRectangle(cursorBrush, 8, 10, 4, 2);
        return bmp;
    }

    private static Image CreateExitIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(0xCC, 0x33, 0x33), 2f);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        g.DrawLine(pen, 4, 4, 12, 12);
        g.DrawLine(pen, 12, 4, 4, 12);
        return bmp;
    }

    private sealed class TrayOwnerMenuActionBinding(ITrayOwner owner)
    {
        public ITrayOwner Owner { get; set; } = owner;
    }
}

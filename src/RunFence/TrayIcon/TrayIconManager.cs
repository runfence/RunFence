using System.Drawing.Drawing2D;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.TrayIcon;

public class TrayIconManager(
    NotifyIcon notifyIcon,
    SidDisplayNameResolver displayNameResolver,
    IIconService iconService,
    ILoggingService log,
    IDatabaseProvider databaseProvider)
    : IDisposable
{
    private ITrayOwner _trayOwner = null!;
    private CredentialStore? _credentialStore;
    private List<StartMenuEntry>? _discoveredEntries;
    private readonly Dictionary<string, Image?> _discoveredIconCache = new(StringComparer.OrdinalIgnoreCase);

    public event Action<AppEntry>? AppLaunchRequested;
    public event Action<string, bool>? FolderBrowserLaunchRequested;
    public event Action<string, bool>? TerminalLaunchRequested;
    public event Action<string, string>? DiscoveredAppLaunchRequested;

    public void Initialize(ITrayOwner trayOwner)
    {
        _trayOwner = trayOwner;

        notifyIcon.Icon = AppIcons.GetAppIcon();
        notifyIcon.Visible = true;
        notifyIcon.MouseClick += OnTrayClick;
        RebuildContextMenu();
    }

    /// <summary>
    /// Re-registers the tray icon with the shell after Explorer restarts (TaskbarCreated).
    /// Windows drops all tray icons when explorer.exe restarts; toggling Visible forces re-registration.
    /// </summary>
    public void RestoreIconVisibility()
    {
        notifyIcon.Visible = false;
        notifyIcon.Visible = true;
    }

    public void UpdateDatabase(CredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
        RebuildContextMenu();
    }

    public void UpdateDiscoveredApps(List<StartMenuEntry>? entries)
    {
        foreach (var icon in _discoveredIconCache.Values)
            icon?.Dispose();
        _discoveredIconCache.Clear();

        _discoveredEntries = entries;
        RebuildContextMenu();
    }

    private void RebuildContextMenu()
    {
        var database = databaseProvider.GetDatabase();
        var oldMenu = notifyIcon.ContextMenuStrip;
        var menu = new ContextMenuStrip { ShowItemToolTips = true };

        var showItem = new ToolStripMenuItem("Show", AppIcons.GetAppIcon().ToBitmap());
        showItem.Click += (_, _) => _trayOwner.TryShowWindow();
        menu.Items.Add(showItem);

        if (database.Apps.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());

            var nameGroups = database.Apps
                .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // By design: configured apps are always launchable from the tray regardless of lock state.
            // This is intentional — lock protects the GUI, not app launching.
            // The tray launch path invokes the orchestrator directly (no lock check), and the IPC
            // handler used by the launcher also authorizes launches independently of the lock.
            foreach (var app in database.Apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                var label = app.Name;
                if (nameGroups.TryGetValue(app.Name, out int count) && count > 1)
                {
                    var accountLabel = app.AppContainerName
                                       ?? displayNameResolver.GetDisplayName(app.AccountSid, null, database.SidNames);
                    label = $"{app.Name} ({accountLabel})";
                }

                var item = new ToolStripMenuItem(label);

                // Add original exe/folder icon to tray menu
                var icon = iconService.GetOriginalAppIcon(app);
                if (icon != null)
                    item.Image = icon;

                var capturedApp = app;
                item.Click += (_, _) => AppLaunchRequested?.Invoke(capturedApp);
                menu.Items.Add(item);
            }
        }

        if (_discoveredEntries?.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            // By design: discovered entries are always launchable from the tray regardless of lock state.
            // The user explicitly opted in per account — lock protects the GUI, not launching.
            var discoveredItems = TrayMenuDiscoveryBuilder.BuildMenuItems(
                _discoveredEntries,
                database.SidNames,
                _discoveredIconCache,
                (exePath, sid) => DiscoveredAppLaunchRequested?.Invoke(exePath, sid),
                displayNameResolver,
                log);
            foreach (var item in discoveredItems)
                menu.Items.Add(item);
        }

        BuildAccountMenuItems(menu, database, database.Accounts.Where(a => a.TrayFolderBrowser).Select(a => a.Sid).ToList(), CreateFolderIcon,
            (sid, shift) => FolderBrowserLaunchRequested?.Invoke(sid, shift));
        BuildAccountMenuItems(menu, database, database.Accounts.Where(a => a.TrayTerminal).Select(a => a.Sid).ToList(), CreateTerminalIcon,
            (sid, shift) => TerminalLaunchRequested?.Invoke(sid, shift));

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit", CreateExitIcon());
        exitItem.Click += (_, _) => Application.Exit();
        menu.Items.Add(exitItem);

        notifyIcon.ContextMenuStrip = menu;
        if (oldMenu != null)
        {
            DisposeMenuItemImages(oldMenu.Items);
            oldMenu.Dispose();
        }
    }

    private void BuildAccountMenuItems(ContextMenuStrip menu, AppDatabase database, List<string> traySids,
        Func<Image> iconFactory, Action<string, bool> onLaunch)
    {
        if (traySids.Count == 0)
            return;

        var credentialSids = _credentialStore?.Credentials
                                 .Select(c => c.Sid)
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase)
                             ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null)
            credentialSids.Add(interactiveSid);

        var sidsToShow = traySids
            .Where(sid => credentialSids.Contains(sid))
            .OrderBy(sid => displayNameResolver.ResolveUsername(sid, database.SidNames)
                            ?? displayNameResolver.GetDisplayName(sid, null, database.SidNames),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sidsToShow.Count == 0)
            return;

        menu.Items.Add(new ToolStripSeparator());
        foreach (var sid in sidsToShow)
        {
            var label = displayNameResolver.ResolveUsername(sid, database.SidNames)
                        ?? displayNameResolver.GetDisplayName(sid, null, database.SidNames);
            var item = new ToolStripMenuItem(label, iconFactory());
            item.ToolTipText = "Hold Shift to launch with full privileges";
            // By design: tray-pinned items are always launchable regardless of lock state.
            // Lock protects the GUI, not launching — same policy as configured app entries.
            var capturedSid = sid;
            item.Click += (_, _) => onLaunch(capturedSid,
                (Control.ModifierKeys & Keys.Shift) != 0);
            menu.Items.Add(item);
        }
    }

    private static void DisposeMenuItemImages(ToolStripItemCollection items)
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

    private static Image CreateFolderIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(0xCC, 0x88, 0x22));
        g.FillRectangle(brush, 1, 5, 5, 2); // folder tab
        g.FillRectangle(brush, 1, 6, 14, 8); // folder body
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

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _trayOwner.TryShowWindow();
        }
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        var menu = notifyIcon.ContextMenuStrip;
        if (menu != null)
        {
            notifyIcon.ContextMenuStrip = null;
            DisposeMenuItemImages(menu.Items);
            menu.Dispose();
        }

        foreach (var icon in _discoveredIconCache.Values)
            icon?.Dispose();
        _discoveredIconCache.Clear();
    }
}
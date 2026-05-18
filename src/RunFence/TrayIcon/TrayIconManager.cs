using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;

namespace RunFence.TrayIcon;

public class TrayIconManager(
    NotifyIcon notifyIcon,
    IAppIconProvider appIconProvider,
    IDatabaseProvider databaseProvider,
    TrayMenuBuilder trayMenuBuilder,
    IInputInjectionBlockerService injectionBlocker)
    : IDisposable, IInputInjectionTraySink
{
    private ITrayOwner _trayOwner = null!;
    private ITrayMenuActionHandler _actionHandler = null!;
    private CredentialStore? _credentialStore;
    private List<StartMenuEntry>? _discoveredEntries;
    private ToolStripMenuItem? _showMenuItem;
    private ToolStripMenuItem? _lockMenuItem;
    private readonly Dictionary<string, Image?> _discoveredIconCache = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _appIconBitmap;
    private bool _initialized;

    public event Action? InputInjectionToggleRequested;

    public void Initialize(ITrayOwner trayOwner, ITrayMenuActionHandler actionHandler)
    {
        _trayOwner = trayOwner;
        _actionHandler = actionHandler;
        _initialized = true;

        notifyIcon.Icon = appIconProvider.GetAppIcon();
        notifyIcon.Visible = true;
        notifyIcon.MouseClick += OnTrayClick;
        notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
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

    public void ShowBalloonTip(string text) =>
        notifyIcon.ShowBalloonTip(5000, "RunFence", text, ToolTipIcon.Warning);

    public void UpdateDatabase(CredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
        if (_initialized)
            RebuildContextMenu();
    }

    public void UpdateDiscoveredApps(List<StartMenuEntry>? entries)
    {
        foreach (var icon in _discoveredIconCache.Values)
            icon?.Dispose();
        _discoveredIconCache.Clear();

        _discoveredEntries = entries;
        if (_initialized)
            RebuildContextMenu();
    }

    private void RebuildContextMenu()
    {
        var database = databaseProvider.GetDatabase();

        var oldMenu = notifyIcon.ContextMenuStrip;
        if (oldMenu != null)
        {
            notifyIcon.ContextMenuStrip = null;
            // Detach the manager-retained app icon bitmap before menu cleanup so it survives rebuilds.
            if (oldMenu.Items.Count > 0 && oldMenu.Items[0].Image == _appIconBitmap)
                oldMenu.Items[0].Image = null;
            trayMenuBuilder.DisposeMenuItemImages(oldMenu.Items);
            oldMenu.Dispose();
        }

        var buildResult = trayMenuBuilder.BuildContextMenu(new TrayMenuBuildRequest(
            _credentialStore,
            _discoveredEntries,
            _discoveredIconCache,
            database,
            _appIconBitmap ??= appIconProvider.GetAppIcon().ToBitmap(),
            _actionHandler));
        var menu = buildResult.Menu;
        menu.Opening += OnContextMenuOpening;

        _showMenuItem = buildResult.ShowItem;
        _lockMenuItem = buildResult.LockItem;
        trayMenuBuilder.ApplyOwnerState(_trayOwner, _showMenuItem, _lockMenuItem);

        var blockInjectionItem = new ToolStripMenuItem("Block Input Injection")
        {
            Checked = injectionBlocker.IsEnabled
        };
        blockInjectionItem.Click += (_, _) => InputInjectionToggleRequested?.Invoke();
        menu.Items.Insert(2, blockInjectionItem);

        notifyIcon.ContextMenuStrip = menu;
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_showMenuItem == null || _lockMenuItem == null)
            return;

        trayMenuBuilder.ApplyOwnerState(_trayOwner, _showMenuItem, _lockMenuItem);
    }

    private async void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            await _trayOwner.TryShowWindowAsync();
    }

    private async void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        await _trayOwner.TryShowWindowAsync();
    }

    public void Dispose()
    {
        notifyIcon.MouseClick -= OnTrayClick;
        notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;

        var menu = notifyIcon.ContextMenuStrip;
        if (menu != null)
        {
            notifyIcon.ContextMenuStrip = null;
            // Detach the manager-retained app icon bitmap before menu cleanup so it is disposed only once below.
            if (menu.Items.Count > 0 && menu.Items[0].Image == _appIconBitmap)
                menu.Items[0].Image = null;
            trayMenuBuilder.DisposeMenuItemImages(menu.Items);
            menu.Dispose();
        }

        foreach (var icon in _discoveredIconCache.Values)
            icon?.Dispose();
        _discoveredIconCache.Clear();

        _appIconBitmap?.Dispose();
        _appIconBitmap = null;
    }
}

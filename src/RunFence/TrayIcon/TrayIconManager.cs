using System.ComponentModel;
using System.Drawing;
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
    IInputInjectionBlockerService injectionBlocker,
    TrayIconOverlayRenderer overlayRenderer)
    : IDisposable, IInputInjectionTraySink, ITrayForegroundMarkerOverlaySink
{
    private ITrayOwner _trayOwner = null!;
    private ITrayMenuActionHandler _actionHandler = null!;
    private CredentialStore? _credentialStore;
    private List<StartMenuEntry>? _discoveredEntries;
    private ToolStripMenuItem? _showMenuItem;
    private ToolStripMenuItem? _lockMenuItem;
    private readonly Dictionary<string, Image?> _discoveredIconCache = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _appIconBitmap;
    private Icon? _baseAppIcon;
    private Icon? _foregroundMarkerOverlayIcon;
    private int? _foregroundMarkerArgb;
    private bool _initialized;
    private bool _disposed;

    public event Action? InputInjectionToggleRequested;

    public void Initialize(ITrayOwner trayOwner, ITrayMenuActionHandler actionHandler)
    {
        _trayOwner = trayOwner;
        _actionHandler = actionHandler;
        _initialized = true;
        _baseAppIcon = appIconProvider.GetAppIcon();

        if (_baseAppIcon != null)
        {
            notifyIcon.Icon = _baseAppIcon;
            GetOrCreateOwnedAppIconBitmap();
        }
        notifyIcon.Visible = true;
        notifyIcon.MouseClick += OnTrayClick;
        notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
        RebuildContextMenu();
        ApplyForegroundMarkerOverlay();
    }

    /// <summary>
    /// Re-registers the tray icon with the shell after Explorer restarts (TaskbarCreated).
    /// Windows drops all tray icons when explorer.exe restarts; toggling Visible forces re-registration.
    /// </summary>
    public void RestoreIconVisibility()
    {
        notifyIcon.Visible = false;
        notifyIcon.Visible = true;
        ApplyForegroundMarkerOverlay();
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
            GetOrCreateOwnedAppIconBitmap(),
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
        if (_disposed)
            return;

        _disposed = true;
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

        if (_foregroundMarkerOverlayIcon is not null)
        {
            if (_baseAppIcon != null)
            {
                try
                {
                    notifyIcon.Icon = _baseAppIcon;
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _foregroundMarkerOverlayIcon.Dispose();
            _foregroundMarkerOverlayIcon = null;
        }

        _appIconBitmap?.Dispose();
        _appIconBitmap = null;
    }

    public void SetForegroundMarkerOverlay(Color? color)
    {
        if (_disposed)
            return;

        var requestedArgb = color?.ToArgb();
        if (_foregroundMarkerArgb == requestedArgb)
            return;

        _foregroundMarkerArgb = requestedArgb;
        ApplyForegroundMarkerOverlay();
    }

    private void ApplyForegroundMarkerOverlay()
    {
        if (_disposed || !_initialized)
            return;

        if (_foregroundMarkerArgb is null)
        {
            ClearForegroundMarkerOverlay();
            return;
        }

        var overlayColor = Color.FromArgb(_foregroundMarkerArgb.Value);
        var overlayIcon = overlayRenderer.CreateOverlayIcon(_baseAppIcon ?? appIconProvider.GetAppIcon(), overlayColor);
        var previousIcon = _foregroundMarkerOverlayIcon;
        notifyIcon.Icon = overlayIcon;
        _foregroundMarkerOverlayIcon = overlayIcon;
        previousIcon?.Dispose();
    }

    private void ClearForegroundMarkerOverlay()
    {
        if (_disposed)
            return;

        if (_baseAppIcon != null)
            notifyIcon.Icon = _baseAppIcon;

        if (_foregroundMarkerOverlayIcon is null)
            return;

        var overlayIcon = _foregroundMarkerOverlayIcon;
        _foregroundMarkerOverlayIcon = null;
        overlayIcon.Dispose();
    }

    private Bitmap GetOrCreateOwnedAppIconBitmap()
    {
        if (_appIconBitmap is not null)
            return _appIconBitmap;

        var icon = _baseAppIcon ?? appIconProvider.GetAppIcon();
        _appIconBitmap = icon.ToBitmap();
        return _appIconBitmap;
    }
}

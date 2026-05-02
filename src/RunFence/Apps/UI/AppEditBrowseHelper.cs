using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Resolution;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles file/folder browse dialogs, path validation, and app discovery for <see cref="AppEditDialog"/>.
/// Returns results without touching dialog controls directly.
/// </summary>
public class AppEditBrowseHelper
{
    private readonly IShortcutDiscoveryService _shortcutDiscoveryService;
    private readonly IShortcutIconHelper _iconHelper;
    private readonly ShortcutTargetResolver _shortcutTargetResolver;
    private readonly ISessionProvider _sessionProvider;
    private readonly IExecutableKindService _executableKindService;

    internal AppEditBrowseHelper(
        IShortcutDiscoveryService shortcutDiscoveryService,
        IShortcutIconHelper iconHelper,
        ShortcutTargetResolver shortcutTargetResolver,
        ISessionProvider sessionProvider,
        IExecutableKindService executableKindService)
    {
        _shortcutDiscoveryService = shortcutDiscoveryService;
        _iconHelper = iconHelper;
        _shortcutTargetResolver = shortcutTargetResolver;
        _sessionProvider = sessionProvider;
        _executableKindService = executableKindService;
    }

    /// <summary>
    /// Shows a folder browser dialog for selecting a working directory.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseWorkingDir(string currentPath)
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select Working Directory";
        dlg.UseDescriptionForTitle = true;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            dlg.InitialDirectory = currentPath;

        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>
    /// Shows the file browse dialog, resolves shortcuts if requested, and applies the result
    /// to the receiver. Does nothing if the dialog is cancelled.
    /// </summary>
    public void BrowseAndApplyFile(IAppEditBrowseResultReceiver receiver)
    {
        var path = BrowseFile();
        if (path == null)
            return;

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            if (MessageBox.Show("Resolve shortcut target?", "RunFence", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                var resolved = TryResolveShortcut(path);
                if (resolved == null)
                {
                    MessageBox.Show(
                        "Could not resolve shortcut target. The shortcut may be broken or reference a removed app entry.",
                        "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                receiver.SetFilePath(resolved.Value.ResolvedPath);
                if (string.IsNullOrWhiteSpace(receiver.GetAppName()))
                    receiver.SetAppName(Path.GetFileNameWithoutExtension(resolved.Value.ResolvedPath));
                if (!string.IsNullOrEmpty(resolved.Value.ShortcutWorkingDirectory))
                    receiver.SetWorkingDir(resolved.Value.ShortcutWorkingDirectory);
                if (!string.IsNullOrEmpty(resolved.Value.ShortcutArgs))
                    receiver.SetDefaultArgs(resolved.Value.ShortcutArgs);
                receiver.SetFolderMode(Directory.Exists(resolved.Value.ResolvedPath));
                return;
            }
        }

        receiver.SetFilePath(path);
        if (string.IsNullOrWhiteSpace(receiver.GetAppName()))
            receiver.SetAppName(Path.GetFileNameWithoutExtension(path));
        receiver.SetFolderMode(false);
    }

    /// <summary>
    /// Shows the folder browse dialog and applies the result to the receiver.
    /// Does nothing if the dialog is cancelled.
    /// </summary>
    public void BrowseAndApplyFolder(IAppEditBrowseResultReceiver receiver)
    {
        var path = BrowseFolder();
        if (path == null)
            return;
        receiver.SetFilePath(path);
        if (string.IsNullOrWhiteSpace(receiver.GetAppName()))
            receiver.SetAppName(Path.GetFileName(path) ?? string.Empty);
        receiver.SetFolderMode(true);
    }

    /// <summary>
    /// Runs app discovery and applies the selected result to the receiver.
    /// Does nothing if discovery returns no results or the user cancels.
    /// </summary>
    public async Task DiscoverAndApplyAsync(IAppEditBrowseResultReceiver receiver)
    {
        var apps = await Task.Run(DiscoverApps);
        var result = await ShowDiscoveryDialogAsync(apps);
        if (result != null)
        {
            receiver.SetFilePath(result.Value.path);
            if (string.IsNullOrWhiteSpace(receiver.GetAppName()) && result.Value.name != null)
                receiver.SetAppName(result.Value.name);
            receiver.SetFolderMode(false);
        }
    }

    private List<DiscoveredApp> DiscoverApps() => _shortcutDiscoveryService.DiscoverApps();

    private async Task<(string path, string? name)?> ShowDiscoveryDialogAsync(List<DiscoveredApp> apps)
    {
        using var dlg = new AppDiscoveryDialog(apps, _iconHelper);
        return await dlg.ShowDialogAsync() == DialogResult.OK
            ? (dlg.SelectedPath!, dlg.SelectedName) : null;
    }

    /// <summary>
    /// If <paramref name="exePath"/> is a known browser or UWP executable and the receiver's
    /// current privilege level allows a suggestion, prompts the user to set it to 'Above Basic'.
    /// Does nothing when the privilege level is already AboveBasic or higher, or for non-exe paths.
    /// </summary>
    public void PromptAboveBasicIfNeeded(string exePath, IAppEditBrowseResultReceiver receiver)
    {
        if (!receiver.CanSuggestAboveBasicPrivilegeLevel())
            return;

        string? message = null;
        if (_executableKindService.IsKnownBrowserExe(exePath))
            message = "This is a browser application. Set privilege level to 'Above Basic' for better compatibility?";
        else if (_executableKindService.IsUwpExeFile(exePath))
            message = "This is a UWP application. Set privilege level to 'Above Basic' for better compatibility? (UWP apps do not support job restrictions.)";

        if (message != null
            && MessageBox.Show(message, "RunFence", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            receiver.SetPrivilegeLevel(PrivilegeLevel.AboveBasic);
    }

    private string? BrowseFile()
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = PathConstants.AppFileDialogFilter;
        dlg.Title = "Select File";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    private string? BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select Folder";
        dlg.UseDescriptionForTitle = true;

        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    private ShortcutTargetResolver.ResolvedShortcut? TryResolveShortcut(string lnkPath)
        => _shortcutTargetResolver.TryResolveShortcut(lnkPath, _sessionProvider.GetSession().Database.Apps);
}

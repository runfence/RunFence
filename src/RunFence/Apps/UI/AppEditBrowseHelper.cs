using RunFence.Apps.Shortcuts;
using RunFence.Core.Infrastructure;
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
    private readonly IAppDiscoveryDialogService _appDiscoveryDialogService;
    private readonly IMessageBoxService _messageBoxService;
    private readonly ShortcutTargetResolver _shortcutTargetResolver;
    private readonly ISessionProvider _sessionProvider;
    private readonly IExecutableKindService _executableKindService;
    private readonly AppEntryHandlerPathSuggestionService _handlerSuggestionService;
    private readonly IOpenFileDialogAdapterFactory _openFileDialogFactory;
    private readonly IFolderBrowserDialogAdapterFactory _folderBrowserDialogFactory;

    public AppEditBrowseHelper(
        IShortcutDiscoveryService shortcutDiscoveryService,
        IShortcutIconHelper iconHelper,
        IAppDiscoveryDialogService appDiscoveryDialogService,
        IMessageBoxService messageBoxService,
        ShortcutTargetResolver shortcutTargetResolver,
        ISessionProvider sessionProvider,
        IExecutableKindService executableKindService,
        AppEntryHandlerPathSuggestionService handlerSuggestionService,
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory)
    {
        _shortcutDiscoveryService = shortcutDiscoveryService;
        _iconHelper = iconHelper;
        _appDiscoveryDialogService = appDiscoveryDialogService;
        _messageBoxService = messageBoxService;
        _shortcutTargetResolver = shortcutTargetResolver;
        _sessionProvider = sessionProvider;
        _executableKindService = executableKindService;
        _handlerSuggestionService = handlerSuggestionService;
        _openFileDialogFactory = openFileDialogFactory;
        _folderBrowserDialogFactory = folderBrowserDialogFactory;
    }

    /// <summary>
    /// Shows a folder browser dialog for selecting a working directory.
    /// Returns the selected path, or null if cancelled.
    /// </summary>
    public string? BrowseWorkingDir(string currentPath)
    {
        using var dlgAdapter = _folderBrowserDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Description = "Select Working Directory";
        dlg.UseDescriptionForTitle = true;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            dlg.InitialDirectory = currentPath;

        return dlgAdapter.ShowDialog(owner: null) == DialogResult.OK ? dlg.SelectedPath : null;
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

        var selectedAccountSid = receiver.GetSelectedAccountSid();
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            if (_messageBoxService.Show(
                    "Resolve shortcut target?",
                    "RunFence",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var resolved = TryResolveShortcut(path);
                if (resolved == null)
                {
                    _messageBoxService.Show(
                        "Could not resolve shortcut target. The shortcut may be broken or reference a removed app entry.",
                        "RunFence",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                path = resolved.Value.ResolvedPath;
                if (string.IsNullOrWhiteSpace(receiver.GetAppName()))
                    receiver.SetAppName(Path.GetFileNameWithoutExtension(path));
                if (!string.IsNullOrEmpty(resolved.Value.ShortcutWorkingDirectory))
                    receiver.SetWorkingDir(resolved.Value.ShortcutWorkingDirectory);
                if (!string.IsNullOrEmpty(resolved.Value.ShortcutArgs))
                    receiver.SetDefaultArgs(resolved.Value.ShortcutArgs);
                receiver.SetFolderMode(Directory.Exists(resolved.Value.ResolvedPath));
            }
        }

        path = ApplyHandlerSuggestionIfAvailable(path, selectedAccountSid);
        receiver.SetFilePath(path);
        if (string.IsNullOrWhiteSpace(receiver.GetAppName()))
            receiver.SetAppName(Path.GetFileNameWithoutExtension(path));

        receiver.SetFolderMode(Directory.Exists(path));
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
    public async Task DiscoverAndApplyAsync(IAppEditBrowseResultReceiver receiver, Func<bool>? canContinue = null)
    {
        var apps = await Task.Run(DiscoverApps);
        if (canContinue != null && !canContinue())
            return;

        var result = ShowDiscoveryDialog(apps);
        if (canContinue != null && !canContinue())
            return;

        if (result != null)
        {
            var selectedAccountSid = receiver.GetSelectedAccountSid();
            var path = ApplyHandlerSuggestionIfAvailable(result.Value.path, selectedAccountSid);
            receiver.SetFilePath(path);
            if (string.IsNullOrWhiteSpace(receiver.GetAppName()) && result.Value.name != null)
                receiver.SetAppName(result.Value.name);
            receiver.SetFolderMode(Directory.Exists(path));
        }
    }

    private List<DiscoveredApp> DiscoverApps() => _shortcutDiscoveryService.DiscoverApps();

    private (string path, string? name)? ShowDiscoveryDialog(List<DiscoveredApp> apps)
        => _appDiscoveryDialogService.ShowDialog(apps, _iconHelper);

    /// <summary>
    /// If <paramref name="exePath"/> is a known browser or UWP executable and the receiver's
    /// current privilege level allows a suggestion, prompts the user to set it to 'Basic'.
    /// Does nothing when the privilege level is already Basic, High Integrity, or Highest Allowed, or for non-exe paths.
    /// </summary>
    public void PromptBasicIfNeeded(string exePath, IAppEditBrowseResultReceiver receiver)
    {
        if (!receiver.CanSuggestBasicPrivilegeLevel())
            return;

        string? message = null;
        if (_executableKindService.IsKnownBrowserExe(exePath))
            message = "This is a browser application. Set privilege level to 'Basic' for better compatibility?";
        else if (_executableKindService.IsUwpExeFile(exePath))
            message = "This is a UWP application. Set privilege level to 'Basic' for better compatibility? (UWP apps do not support job restrictions.)";

        if (message != null
            && _messageBoxService.Show(message, "RunFence", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            receiver.SetPrivilegeLevel(PrivilegeLevel.Basic);
    }

    private string? BrowseFile()
    {
        using var dlgAdapter = _openFileDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Filter = PathConstants.AppFileDialogFilter;
        dlg.Title = "Select File";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        return dlgAdapter.ShowDialog(owner: null) == DialogResult.OK ? dlg.FileName : null;
    }

    private string? BrowseFolder()
    {
        using var dlgAdapter = _folderBrowserDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Description = "Select Folder";
        dlg.UseDescriptionForTitle = true;

        return dlgAdapter.ShowDialog(owner: null) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    private ShortcutTargetResolver.ResolvedShortcut? TryResolveShortcut(string lnkPath)
        => _shortcutTargetResolver.TryResolveShortcut(lnkPath, _sessionProvider.GetSession().Database.Apps);

    private string ApplyHandlerSuggestionIfAvailable(string selectedPath, string? targetAccountSid)
    {
        if (_handlerSuggestionService.TrySuggest(selectedPath, targetAccountSid, out var suggestion)
            && _messageBoxService.Show(
                suggestion.PromptText,
                "RunFence",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
        {
            return suggestion.ReplacementPath;
        }

        return selectedPath;
    }
}

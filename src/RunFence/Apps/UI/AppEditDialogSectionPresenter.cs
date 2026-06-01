namespace RunFence.Apps.UI;

public class AppEditDialogSectionPresenter
{
    private readonly IAppEditDialogSectionsView _view;
    private readonly IAppEditDialogSnapshotView _snapshotView;
    private readonly AppEditDialogSnapshotProvider _snapshotProvider;
    private IReadOnlyList<HandlerAssociationItem>? _currentAssociations;
    private IReadOnlyList<string>? _currentPathPrefixes;
    private bool _isUrlScheme;

    public AppEditDialogSectionPresenter(
        IAppEditDialogSectionsView view,
        IAppEditDialogSnapshotView snapshotView,
        AppEditDialogSnapshotProvider snapshotProvider)
    {
        _view = view;
        _snapshotView = snapshotView;
        _snapshotProvider = snapshotProvider;
    }

    public void InitializeSections(AppEditDialogInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var handlerMappings = snapshot.HandlerMappings?.ToList();
        var pathPrefixes = snapshot.AppPathPrefixes?.ToList();

        if (!SameAssociations(_currentAssociations, handlerMappings))
            _view.SetAssociations(handlerMappings);
        if (!SamePrefixes(_currentPathPrefixes, pathPrefixes))
            _view.SetPathPrefixes(pathPrefixes);

        _currentAssociations = handlerMappings;
        _currentPathPrefixes = pathPrefixes;
        _isUrlScheme = snapshot.IsUrlScheme;
        _view.SetHandlerContext(snapshot.FilePathText.Trim(), snapshot.SelectedAccountSid);
    }

    public void RefreshForSelectedAccount(string? accountSid)
    {
        var snapshot = _snapshotProvider.CaptureInputSnapshot(_snapshotView, _view);
        _view.SetHandlerContext(snapshot.FilePathText.Trim(), accountSid);
    }

    public void ApplySectionVisibility(AppEditDialogMode mode, bool isFolderApp)
    {
        var sectionEnabled = mode switch
        {
            AppEditDialogMode.New => !_isUrlScheme && !isFolderApp,
            AppEditDialogMode.Edit => !_isUrlScheme && !isFolderApp,
            _ => throw new InvalidOperationException($"Unsupported dialog mode '{mode}'.")
        };

        _view.SetEnvironmentEnabled(sectionEnabled);
        _view.SetAssociationsEnabled(sectionEnabled);
        _view.SetPathPrefixesEnabled(!isFolderApp);
        _view.SetPathPrefixTooltip(
            _isUrlScheme ? "URL-scheme prefixes filter which URLs this app handles." : null);
    }

    private static bool SameAssociations(
        IReadOnlyList<HandlerAssociationItem>? left,
        IReadOnlyList<HandlerAssociationItem>? right)
    {
        left ??= [];
        right ??= [];
        return left.SequenceEqual(right);
    }

    private static bool SamePrefixes(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        left ??= [];
        right ??= [];
        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }
}

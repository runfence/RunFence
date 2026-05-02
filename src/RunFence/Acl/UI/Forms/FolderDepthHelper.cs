using RunFence.Core;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Manages folder-depth combo population and selection for <see cref="AclConfigSection"/>.
/// Computes the sequence of ancestor paths from the exe/folder path up to
/// <see cref="PathConstants.MaxFolderAclDepth"/> steps, filtering blocked paths.
/// </summary>
public class FolderDepthHelper(IAclService aclService, ILoggingService log)
{
    private readonly List<string> _folderDepthPaths = new();
    private readonly List<int> _folderDepthIndices = new();

    /// <summary>
    /// Refreshes the folder-depth combo for the given path and folder mode.
    /// Clears and repopulates the combo items, paths, and indices.
    /// Selects the first item; the caller is responsible for refreshing the ACL path label.
    /// </summary>
    public void UpdateFolderDepthCombo(ComboBox folderDepthComboBox, string? exePath, bool isFolder)
    {
        folderDepthComboBox.Items.Clear();
        _folderDepthPaths.Clear();
        _folderDepthIndices.Clear();

        if (string.IsNullOrEmpty(exePath) || PathHelper.IsUrlScheme(exePath) ||
            (isFolder ? !Directory.Exists(exePath) : !File.Exists(exePath)))
        {
            folderDepthComboBox.Items.Add(isFolder ? "(select folder first)" : "(select file first)");
            folderDepthComboBox.SelectedIndex = 0;
            return;
        }

        try
        {
            var folder = isFolder
                ? Path.GetFullPath(exePath)
                : Path.GetDirectoryName(Path.GetFullPath(exePath))!;
            for (int depth = 0; depth <= PathConstants.MaxFolderAclDepth; depth++)
            {
                if (!aclService.IsBlockedPath(folder))
                {
                    _folderDepthPaths.Add(folder);
                    _folderDepthIndices.Add(depth);
                    folderDepthComboBox.Items.Add(
                        Path.GetFileName(folder) is { Length: > 0 } name ? name : folder);
                }

                var parent = Path.GetDirectoryName(folder);
                if (parent == null)
                    break;
                folder = parent;
            }
        }
        catch (Exception ex)
        {
            log.Debug($"UpdateFolderDepthCombo: path resolution failed for '{exePath}': {ex.Message}");
            folderDepthComboBox.Items.Add("(invalid path)");
        }

        if (folderDepthComboBox.Items.Count > 0)
            folderDepthComboBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Returns the display path for the currently selected depth index, or null if none selected.
    /// </summary>
    public string? GetSelectedPath(int selectedIndex)
    {
        if (selectedIndex >= 0 && selectedIndex < _folderDepthPaths.Count)
            return _folderDepthPaths[selectedIndex];
        return null;
    }

    /// <summary>
    /// Returns the depth value for the currently selected combo index, or 0 if not available.
    /// </summary>
    public int GetSelectedDepth(int selectedIndex)
    {
        if (selectedIndex >= 0 && selectedIndex < _folderDepthIndices.Count)
            return _folderDepthIndices[selectedIndex];
        return 0;
    }

    /// <summary>
    /// Selects the combo item corresponding to <paramref name="folderAclDepth"/> in the combo box.
    /// </summary>
    public void SelectFolderDepth(ComboBox folderDepthComboBox, int folderAclDepth)
    {
        var depthIdx = _folderDepthIndices.IndexOf(folderAclDepth);
        if (depthIdx >= 0)
            folderDepthComboBox.SelectedIndex = depthIdx;
    }
}

namespace RunFence.Acl.UI;

/// <summary>
/// Detects NTFS reparse points (junctions, symbolic links) and prompts the user for
/// how to resolve the path when adding entries to ACL Manager.
/// </summary>
public static class ReparsePointPromptHelper
{
    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the paths to add for <paramref name="path"/>:
    /// <list type="bullet">
    ///   <item>Not a reparse point → <c>[path]</c> unchanged.</item>
    ///   <item>User chooses "Add Target Path" → <c>[target]</c>.</item>
    ///   <item>User chooses "Add Both" → <c>[path, target]</c>.</item>
    ///   <item>User cancels, or target cannot be resolved → <c>[]</c> (nothing added).</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> ResolveForAdd(string path, IWin32Window owner)
    {
        if (!IsReparsePoint(path))
            return [path];

        string? target = TryResolveTarget(path);
        if (target == null)
        {
            MessageBox.Show(
                $"Could not resolve the reparse point target for:\n{path}\n\nThe path will not be added.",
                "Reparse Point", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return [];
        }

        target = Path.GetFullPath(target);

        var addTargetButton = new TaskDialogButton("Add Target Path");
        var addBothButton = new TaskDialogButton("Add Both");
        var cancelButton = new TaskDialogButton("Cancel");

        var page = new TaskDialogPage
        {
            Caption = "Reparse Point",
            Heading = "This path is a junction or symbolic link.",
            Text = $"Path:    {path}\nTarget:  {target}",
            Buttons = { addTargetButton, addBothButton, cancelButton },
            DefaultButton = addTargetButton
        };

        var result = TaskDialog.ShowDialog(owner, page);
        if (result == cancelButton)
            return [];
        if (result == addBothButton)
            return [path, target];
        return [target];
    }

    private static string? TryResolveTarget(string path)
    {
        try
        {
            FileSystemInfo? info = Directory.Exists(path)
                ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                  ?? Directory.ResolveLinkTarget(path, returnFinalTarget: false)
                : File.ResolveLinkTarget(path, returnFinalTarget: true)
                  ?? File.ResolveLinkTarget(path, returnFinalTarget: false);
            return info?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
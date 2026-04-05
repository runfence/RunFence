using System.Security.AccessControl;
using RunFence.Acl.UI.Forms;

namespace RunFence.Acl.UI;

/// <summary>Path and rights chosen by the user in <see cref="AclPermissionDialogHelper.ShowAncestorPermissionDialog"/>.</summary>
public record AncestorPermissionResult(string Path, FileSystemRights Rights);

public static class AclPermissionDialogHelper
{
    /// <summary>
    /// Shows a dialog that lets the user select one of the <paramref name="ancestors"/> to grant
    /// permissions on, using a combo box (same style as AclConfigSection's folder depth).
    /// A "Grant Write too" button is shown when <paramref name="rights"/> does not already include write.
    /// </summary>
    /// <returns>The chosen path and rights, or <c>null</c> to skip. Throws
    /// <see cref="OperationCanceledException"/> if the user cancels.</returns>
    public static AncestorPermissionResult? ShowAncestorPermissionDialog(
        IWin32Window? owner, string heading, IReadOnlyList<string> ancestors,
        FileSystemRights rights, string skipButtonText = "Launch Without")
    {
        if (ancestors.Count == 0)
            return null;

        using var dlg = new AncestorPermissionDialog(heading, ancestors, rights, skipButtonText);
        var result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();

        return result switch
        {
            DialogResult.Cancel => throw new OperationCanceledException(),
            DialogResult.No => null,
            _ => dlg.SelectedPath != null ? new AncestorPermissionResult(dlg.SelectedPath, dlg.GrantedRights) : null
        };
    }

    /// <summary>Returns true = grant, false = skip, null = cancel.</summary>
    public static bool? ShowPermissionDialog(IWin32Window? owner, string heading, string text,
        string skipButtonText = "Launch Without")
    {
        var addPermsBtn = new TaskDialogButton("Add Permissions");
        var skipBtn = new TaskDialogButton(skipButtonText);
        var cancelBtn = new TaskDialogButton("Cancel");

        var page = new TaskDialogPage
        {
            Caption = "Permission Required",
            Heading = heading,
            Text = text + "\n\nYou can add permissions, proceed without them, or cancel.",
            Icon = TaskDialogIcon.ShieldWarningYellowBar,
            Buttons = { addPermsBtn, skipBtn, cancelBtn },
            DefaultButton = addPermsBtn
        };

        var result = owner != null
            ? TaskDialog.ShowDialog(owner, page)
            : TaskDialog.ShowDialog(page);
        if (result == addPermsBtn)
            return true;
        if (result == cancelBtn)
            return null;
        return false;
    }
}
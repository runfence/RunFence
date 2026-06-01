using System.Security.AccessControl;
using RunFence.Account;
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

    /// <summary>
    /// Creates a permission prompt delegate that shows a dialog with the account or container
    /// display name resolved via <paramref name="sidNameCache"/>. The prompt returns <c>true</c>
    /// to grant permissions or throws <see cref="OperationCanceledException"/> if the user cancels.
    /// </summary>
    public static Func<string, string, bool> CreateLaunchPermissionPrompt(
        ISidNameCacheService sidNameCache, IWin32Window? owner = null)
        => (path, sid) => ShowPermissionDialog(
                owner,
                new PermissionPromptModel(
                    Caption: "Permission Required",
                    Heading: "Missing permissions",
                    BodyText: $"'{sidNameCache.GetDisplayName(sid)}' needs access to:\n{path}\n\nYou can add permissions, proceed without them, or cancel.",
                    ConfirmButtonText: "Add Permissions",
                    SkipButtonText: "Launch Without",
                    CancelButtonText: "Cancel"))
            ?? throw new OperationCanceledException();

    /// <summary>Returns true = grant, false = skip, null = cancel.</summary>
    public static bool? ShowPermissionDialog(IWin32Window? owner, string heading, string text,
        string skipButtonText = "Launch Without")
        => ShowPermissionDialog(
            owner,
            new PermissionPromptModel(
                Caption: "Permission Required",
                Heading: heading,
                BodyText: text + "\n\nYou can add permissions, proceed without them, or cancel.",
                ConfirmButtonText: "Add Permissions",
                SkipButtonText: skipButtonText,
                CancelButtonText: "Cancel"));

    public static bool? ShowPermissionDialog(IWin32Window? owner, PermissionPromptModel prompt)
    {
        var confirmButton = new TaskDialogButton(prompt.ConfirmButtonText);
        var skipButton = new TaskDialogButton(prompt.SkipButtonText);
        var cancelButton = new TaskDialogButton(prompt.CancelButtonText);

        var page = new TaskDialogPage
        {
            Caption = prompt.Caption,
            Heading = prompt.Heading,
            Text = prompt.BodyText,
            Icon = TaskDialogIcon.ShieldWarningYellowBar,
            Buttons = { confirmButton, skipButton, cancelButton },
            DefaultButton = confirmButton,
            AllowCancel = true
        };

        var result = owner != null
            ? TaskDialog.ShowDialog(owner, page)
            : TaskDialog.ShowDialog(page);
        if (result == confirmButton)
            return true;
        if (result == cancelButton)
            return null;
        return false;
    }
}

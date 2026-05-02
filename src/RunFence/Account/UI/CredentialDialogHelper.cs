using RunFence.Core;
using RunFence.Account.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Encapsulates the common flow for showing a <see cref="CredentialEditDialog"/> on the secure
/// desktop and capturing its results, used by both add and edit credential operations.
/// </summary>
public class CredentialDialogHelper(
    IModalCoordinator modalCoordinator,
    Func<CredentialEditDialog> credentialEditDialogFactory)
{
    public record struct CredentialDialogResult(
        DialogResult Result,
        string? Sid,
        string Username,
        ProtectedString? Password,
        bool OpenCreateUser,
        ProtectedString? CapturedPassword);

    /// <summary>
    /// Shows the credential dialog in add mode (no existing credential).
    /// </summary>
    public CredentialDialogResult ShowAddDialog(
        IReadOnlyList<LocalUserAccount> localUsers,
        string? defaultUsername,
        IReadOnlyDictionary<string, string>? sidNames,
        IReadOnlyCollection<string>? existingSids)
    {
        DialogResult result = DialogResult.None;
        string? sid = null;
        string username = "";
        ProtectedString? password = null;
        bool openCreateUser = false;
        ProtectedString? capturedPassword = null;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = credentialEditDialogFactory();
            dlg.Initialize(localUsers: localUsers, defaultUsername: defaultUsername,
                sidNames: sidNames, existingSids: existingSids);
            result = dlg.ShowDialog();
            sid = dlg.Sid;
            username = dlg.Username;
            password = dlg.Password;
            openCreateUser = dlg.OpenCreateUser;
            capturedPassword = dlg.CapturedPassword;
            dlg.CapturedPassword = null;
        });

        return new CredentialDialogResult(result, sid, username, password, openCreateUser, capturedPassword);
    }

    /// <summary>
    /// Shows the credential dialog in edit mode for an existing credential entry.
    /// </summary>
    public CredentialDialogResult ShowEditDialog(
        CredentialEntry existing,
        IReadOnlyList<LocalUserAccount> localUsers,
        IReadOnlyDictionary<string, string>? sidNames)
    {
        DialogResult result = DialogResult.None;
        string? sid = null;
        string username = "";
        ProtectedString? password = null;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = credentialEditDialogFactory();
            dlg.Initialize(existing, localUsers, sidNames: sidNames);
            result = dlg.ShowDialog();
            sid = dlg.Sid;
            username = dlg.Username;
            password = dlg.Password;
        });

        return new CredentialDialogResult(result, sid, username, password,
            OpenCreateUser: false, CapturedPassword: null);
    }
}

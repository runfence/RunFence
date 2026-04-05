using System.Security;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Account.UI;

public class AccountLaunchOrchestrator(
    IAccountLauncher launcher,
    IAccountCredentialManager credentialManager,
    ILoggingService log,
    IFolderHandlerService folderHandlerService)
{
    public void LaunchCmd(AccountRow accountRow, CredentialStore store, ProtectedBuffer key,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
    {
        var terminalExe = launcher.ResolveTerminalExe(accountRow.Sid);
        var label = terminalExe == "cmd.exe" ? "CMD" : "Windows Terminal";
        Launch(label, accountRow, store, key, (password, cred) => launcher.LaunchCmd(password, cred, sidNames, flags));
    }

    public void LaunchFolderBrowser(AccountRow accountRow, CredentialStore store, ProtectedBuffer key,
        AppSettings settings, IWin32Window? parent, IReadOnlyDictionary<string, string>? sidNames,
        LaunchFlags flags = default, AppDatabase? db = null)
    {
        Launch("Folder Browser", accountRow, store, key, (password, cred) =>
        {
            bool granted = launcher.LaunchFolderBrowser(password, cred, settings, sidNames, flags);
            if (granted && db != null)
                credentialManager.SaveConfig(db, key, store.ArgonSalt);
        });
    }

    public void LaunchEnvironmentVariables(AccountRow accountRow, CredentialStore store, ProtectedBuffer key,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
        => Launch("Environment Variables", accountRow, store, key, (password, cred) => launcher.LaunchEnvironmentVariables(password, cred, sidNames, flags));

    public void InstallPackage(InstallablePackage package, AccountRow accountRow, CredentialStore store, ProtectedBuffer key,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
        => Launch($"Install {package.DisplayName}", accountRow, store, key,
            (password, cred) => launcher.InstallPackage(package, password, cred, sidNames, flags));

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, AccountRow accountRow, CredentialStore store, ProtectedBuffer key,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
        => Launch("Install packages", accountRow, store, key,
            (password, cred) => launcher.InstallPackages(packages, password, cred, sidNames, flags));

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, CredentialEntry credEntry, SecureString? password,
        IReadOnlyDictionary<string, string>? sidNames, LaunchFlags flags = default)
    {
        try
        {
            launcher.InstallPackages(packages, password, credEntry, sidNames, flags);
            folderHandlerService.Register(credEntry.Sid);
        }
        catch (Exception ex)
        {
            log.Error("Failed to install packages", ex);
            MessageBox.Show($"Failed to install packages: {ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void ToggleFolderBrowserTray(AccountRow accountRow, AppDatabase db, CredentialStore store, ProtectedBuffer key, Action onSaved)
        => ToggleTrayFlag(db, accountRow.Sid, a => a.TrayFolderBrowser, (a, v) => a.TrayFolderBrowser = v, store, key, onSaved);

    public void ToggleDiscoveryTray(AccountRow accountRow, AppDatabase db, CredentialStore store, ProtectedBuffer key, Action onSaved)
        => ToggleTrayFlag(db, accountRow.Sid, a => a.TrayDiscovery, (a, v) => a.TrayDiscovery = v, store, key, onSaved);

    public void ToggleTerminalTray(AccountRow accountRow, AppDatabase db, CredentialStore store, ProtectedBuffer key, Action onSaved)
        => ToggleTrayFlag(db, accountRow.Sid, a => a.TrayTerminal, (a, v) => a.TrayTerminal = v, store, key, onSaved);

    private void ToggleTrayFlag(AppDatabase db, string sid,
        Func<AccountEntry, bool> getter, Action<AccountEntry, bool> setter,
        CredentialStore store, ProtectedBuffer key, Action onSaved)
    {
        var acct = db.GetOrCreateAccount(sid);
        setter(acct, !getter(acct));
        db.RemoveAccountIfEmpty(sid);
        credentialManager.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
    }

    private void Launch(string label, AccountRow accountRow, CredentialStore store, ProtectedBuffer key, Action<SecureString?, CredentialEntry> launchAction)
    {
        var credEntry = accountRow.Credential;

        // Current account and interactive user (with or without stored credential) launch without password.
        // For interactive user without a stored credential, create a transient entry for domain/username resolution.
        if (SidResolutionHelper.CanLaunchWithoutPassword(accountRow.Sid))
        {
            var effectiveCred = credEntry ?? new CredentialEntry { Sid = accountRow.Sid };
            try
            {
                launchAction(null, effectiveCred);
                folderHandlerService.Register(accountRow.Sid);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.Error($"Failed to launch {label}", ex);
                MessageBox.Show($"Failed to launch {label}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return;
        }

        if (credEntry == null)
            return;

        SecureString? password = null;
        try
        {
            var status = credentialManager.DecryptCredential(credEntry.Sid, store, key, out password);
            switch (status)
            {
                case CredentialLookupStatus.NotFound:
                    MessageBox.Show("Credential not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                case CredentialLookupStatus.MissingPassword:
                    MessageBox.Show("No password stored for this account.", "Missing Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                default:
                    launchAction(password, credEntry);
                    folderHandlerService.Register(accountRow.Sid);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error($"Failed to launch {label}", ex);
            MessageBox.Show($"Failed to launch {label}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            password?.Dispose();
        }
    }
}
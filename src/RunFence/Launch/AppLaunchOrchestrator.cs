using System.Security;
using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.Security;

namespace RunFence.Launch;

public class CredentialNotFoundException(string message) : Exception(message);

public class MissingPasswordException(string message) : Exception(message);

public class AppLaunchOrchestrator(
    IProcessLaunchService processLaunchService,
    ICredentialEncryptionService encryptionService,
    ISidResolver sidResolver,
    IPermissionGrantService permissionGrantService,
    IAccountLauncher accountLauncher,
    IAppContainerService appContainerService,
    IFolderHandlerService folderHandlerService,
    IDatabaseService databaseService)
    : IAppLaunchOrchestrator
{
    // Set via SetData(); accessed only on the UI thread.
    private AppDatabase _database = null!;
    private CredentialStore _credentialStore = null!;
    private ProtectedBuffer _pinDerivedKey = null!;

    public void SetData(SessionContext session)
    {
        _database = session.Database;
        _credentialStore = session.CredentialStore;
        _pinDerivedKey = session.PinDerivedKey;
    }

    public void Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null)
        => LaunchCore(app, launcherArguments, launcherWorkingDirectory);

    private void LaunchCore(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory)
    {
        // AppContainer path: bypass credential resolution entirely
        if (app.AppContainerName != null)
        {
            var entry = _database.AppContainers
                            .FirstOrDefault(c => string.Equals(c.Name, app.AppContainerName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"AppContainer '{app.AppContainerName}' not found in database.");

            // EnsureTraverseAccess is called here (not inside Launch) so we can
            // persist newly tracked paths — Launch itself has no config-save context.
            var parentFolder = Path.GetDirectoryName(Path.GetFullPath(app.ExePath));
            if (parentFolder != null && appContainerService.EnsureTraverseAccess(entry, parentFolder).Modified)
            {
                using var scope = _pinDerivedKey.Unprotect();
                databaseService.SaveConfig(_database, scope.Data, _credentialStore.ArgonSalt);
            }

            appContainerService.Launch(app, entry, launcherArguments, launcherWorkingDirectory);
            return;
        }

        // DO NOT check target exists here - admin might be denied in path ACL and it's intended

        LaunchAsAccount(app.AccountSid, creds =>
        {
            var defaults = LaunchFlags.FromAccountDefaults(_database, app.AccountSid);
            var useSplitToken = app.RunAsSplitToken ?? defaults.UseSplitToken;
            var useLowIntegrity = app.LaunchAsLowIntegrity ?? defaults.UseLowIntegrity;
            processLaunchService.Launch(app, creds,
                launcherArguments, launcherWorkingDirectory, _database.Settings,
                new LaunchFlags(useSplitToken, useLowIntegrity));
        });
    }

    public void LaunchFolderBrowser(string accountSid, string folderPath,
        bool? launchAsLowIntegrity = null, bool? useSplitToken = null)
    {
        LaunchAsAccount(accountSid, creds =>
        {
            var settings = _database.Settings;
            var defaults = LaunchFlags.FromAccountDefaults(_database, accountSid);
            var flags = new LaunchFlags(
                useSplitToken ?? defaults.UseSplitToken,
                launchAsLowIntegrity ?? defaults.UseLowIntegrity);
            processLaunchService.LaunchFolder(folderPath, settings.FolderBrowserExePath,
                settings.FolderBrowserArguments, creds, flags);
        });
    }

    public void LaunchFolderBrowserFromTray(string accountSid, Func<string, bool?>? permissionPrompt = null,
        bool? useSplitToken = null, bool? useLowIntegrity = null)
    {
        LaunchAsAccount(accountSid, creds =>
        {
            var isCurrentAccount = creds.TokenSource == LaunchTokenSource.CurrentProcess;
            var startMenuPath = sidResolver.TryGetStartMenuProgramsPath(accountSid, isCurrentAccount)
                                ?? throw new InvalidOperationException(
                                    $"Profile path not found in registry for SID {accountSid}.");

            var settings = _database.Settings;
            var folderBrowserExe = PathHelper.ResolveExePath(settings.FolderBrowserExePath);

            if (!isCurrentAccount && !string.IsNullOrEmpty(folderBrowserExe) && File.Exists(folderBrowserExe))
            {
                Func<string, string, bool>? convertedConfirm = permissionPrompt != null
                    ? PermissionGrantService.AdaptConfirm(permissionPrompt)
                    : null;
                if (permissionGrantService.EnsureExeDirectoryAccess(
                        folderBrowserExe, accountSid, convertedConfirm).DatabaseModified)
                {
                    using var scope = _pinDerivedKey.Unprotect();
                    databaseService.SaveConfig(_database, scope.Data, _credentialStore.ArgonSalt);
                }
            }

            var defaults = LaunchFlags.FromAccountDefaults(_database, accountSid);
            var flags = new LaunchFlags(
                useSplitToken ?? defaults.UseSplitToken,
                useLowIntegrity ?? defaults.UseLowIntegrity);
            processLaunchService.LaunchFolder(
                startMenuPath, folderBrowserExe, settings.FolderBrowserArguments, creds, flags);
        });
    }

    public void LaunchTerminalFromTray(string accountSid, bool? useSplitToken = null, bool? useLowIntegrity = null)
    {
        LaunchAsAccount(accountSid, creds =>
        {
            var defaults = LaunchFlags.FromAccountDefaults(_database, accountSid);
            var flags = new LaunchFlags(
                useSplitToken ?? defaults.UseSplitToken,
                useLowIntegrity ?? defaults.UseLowIntegrity);
            var terminalExe = accountLauncher.ResolveTerminalExe(accountSid);
            var profilePath = creds.TokenSource == LaunchTokenSource.CurrentProcess
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : sidResolver.TryGetProfilePath(accountSid);
            processLaunchService.LaunchExe(new ProcessLaunchTarget(terminalExe, WorkingDirectory: profilePath), creds, flags);
        });
    }

    public void LaunchDiscoveredApp(string exePath, string accountSid)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"File not found: {exePath}");

        LaunchAsAccount(accountSid, creds =>
        {
            var flags = LaunchFlags.FromAccountDefaults(_database, accountSid);
            processLaunchService.LaunchExe(new ProcessLaunchTarget(exePath, WorkingDirectory: Path.GetDirectoryName(exePath)), creds, flags);
        });
    }

    public void LaunchExe(string exePath, string accountSid, List<string>? arguments = null, string? workingDirectory = null)
    {
        LaunchAsAccount(accountSid, creds =>
        {
            var flags = LaunchFlags.FromAccountDefaults(_database, accountSid);
            processLaunchService.LaunchExe(new ProcessLaunchTarget(exePath, CommandLineHelper.JoinArgs(arguments), workingDirectory), creds, flags);
        });
    }

    public int LaunchExeReturnPid(string exePath, string accountSid, List<string>? arguments = null,
        string? workingDirectory = null, Func<string, bool?>? permissionPrompt = null,
        bool? useSplitToken = null, bool hideWindow = false)
    {
        return LaunchAsAccount(accountSid, creds =>
        {
            var defaults = LaunchFlags.FromAccountDefaults(_database, accountSid);
            var flags = defaults with { UseSplitToken = useSplitToken ?? defaults.UseSplitToken };

            Func<string, string, bool>? confirm = permissionPrompt != null
                ? PermissionGrantService.AdaptConfirm(permissionPrompt)
                : null;
            if (permissionGrantService.EnsureExeDirectoryAccess(exePath, accountSid, confirm).DatabaseModified)
            {
                using var scope = _pinDerivedKey.Unprotect();
                databaseService.SaveConfig(_database, scope.Data, _credentialStore.ArgonSalt);
            }

            return processLaunchService.LaunchExeReturnPid(new ProcessLaunchTarget(exePath, CommandLineHelper.JoinArgs(arguments), workingDirectory, HideWindow: hideWindow), creds, flags);
        });
    }

    private void LaunchAsAccount(string accountSid, Action<LaunchCredentials> launch)
    {
        SecureString? password = null;
        try
        {
            var creds = DecryptAndResolve(accountSid);
            password = creds.Password;
            launch(creds);
            folderHandlerService.Register(accountSid);
        }
        finally
        {
            password?.Dispose();
        }
    }

    private T LaunchAsAccount<T>(string accountSid, Func<LaunchCredentials, T> launch)
    {
        SecureString? password = null;
        try
        {
            var creds = DecryptAndResolve(accountSid);
            password = creds.Password;
            T result = launch(creds);
            folderHandlerService.Register(accountSid);
            return result;
        }
        finally
        {
            password?.Dispose();
        }
    }

    private LaunchCredentials DecryptAndResolve(string accountSid)
    {
        using var scope = _pinDerivedKey.Unprotect();
        return CredentialHelper.DecryptAndResolve(
                   accountSid, _credentialStore, encryptionService, scope.Data,
                   sidResolver, _database.SidNames, out var status)
               ?? throw (status == CredentialLookupStatus.NotFound
                   ? new CredentialNotFoundException("Account not found.")
                   : new MissingPasswordException("No password stored for this account."));
    }
}
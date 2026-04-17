using System.Security.Cryptography;
using System.Security.Principal;
using Autofac;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.UI.Forms;

namespace RunFence;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ILoggingService? log = null;

        void LogFatal(string message)
        {
            if (log != null)
            {
                log.Fatal(message);
                return;
            }
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [FATAL] {message}{Environment.NewLine}";
                File.AppendAllText(Constants.LogFilePath, line);
            }
            catch { }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var message = e.ExceptionObject is Exception ex
                ? $"Unhandled exception: {ex}"
                : $"Unhandled exception: {e.ExceptionObject}";
            LogFatal(message);
        };

        Application.ThreadException += (_, e) =>
            LogFatal($"Unhandled UI exception: {e.Exception}");

        using var windowSecurity = new WindowSecurityService();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (EarlyExitArgsHandler.HandleEarlyExitArgs(args))
            return;

        var isBackground = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        var identity = WindowsIdentity.GetCurrent();

        // Build the foundation container (pre-session). Side-effect-free — no I/O during Build().
        using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();

        log = foundationContainer.Resolve<ILoggingService>();
        var databaseService = foundationContainer.Resolve<IDatabaseService>();
        var configPaths = foundationContainer.Resolve<IConfigPaths>();
        var appInit = foundationContainer.Resolve<IAppInitializationHelper>();
        var startupUi = foundationContainer.Resolve<IStartupUI>();
        var encryptionService = foundationContainer.Resolve<ICredentialEncryptionService>();

        SidResolutionHelper.InitializeInteractiveUserSid();
        if (!new ElevationChecker(startupUi).CheckElevation())
            return;

        // Enable required privileges once for the lifetime of this always-elevated admin process.
        // Each privilege is enabled independently so that failure of one (e.g. not held by this
        // token) does not roll back the others — matching the TokenTest's per-privilege approach.
        // Non-default privileges are stripped from child process tokens (LaunchTokenSource.CurrentProcess)
        // via PrivilegesToDisable in CreateProcessLauncherHelper.
        foreach (var priv in new[]
        {
            TokenPrivilegeHelper.SeBackupPrivilege,
            TokenPrivilegeHelper.SeRestorePrivilege,
            TokenPrivilegeHelper.SeTakeOwnershipPrivilege,
            TokenPrivilegeHelper.SeImpersonatePrivilege,
            TokenPrivilegeHelper.SeIncreaseQuotaPrivilege,
            TokenPrivilegeHelper.SeDebugPrivilege,
        })
        {
            try { TokenPrivilegeHelper.EnablePrivileges([priv]); }
            catch (Exception ex) { log.Warn($"Could not enable {priv} at startup: {ex.Message}"); }
        }

        var singleInstance = new SingleInstanceService();
        if (!new SessionAcquisitionHandler(startupUi, configPaths).AcquireMutexOrTakeover(singleInstance, isBackground, log))
            return;

        try
        {
            log.Info($"Running as: {identity.Name}");

            var credResult = new StartupCredentialLoader(startupUi, databaseService, configPaths)
                .LoadAndVerifyCredentials(encryptionService, log);
            if (credResult == null)
                return;

            var credentialStore = credResult.Store;
            var mismatchKey = credResult.MismatchKey;
            var protectedKey = new ProtectedBuffer(credResult.PinDerivedKey);
            try
            {
                AppDatabase database;

                // Startup operations use scoped unprotect, closed before Application.Run()
                using (var startupScope = protectedKey.Unprotect())
                {
                    // Try loading config with mismatch key (different salt from another machine/session)
                    AppDatabase? mismatchDatabase = null;
                    if (mismatchKey != null)
                    {
                        try
                        {
                            mismatchDatabase = databaseService.LoadConfig(mismatchKey);
                            appInit.NormalizeAccountSids(mismatchDatabase.Apps, SidResolutionHelper.GetCurrentUserSid());
                            // Re-encrypt with the current session's key and salt
                            databaseService.SaveConfig(mismatchDatabase, startupScope.Data, credentialStore.ArgonSalt);
                        }
                        catch (Exception)
                        {
                            mismatchDatabase = null;
                        } // wrong PIN for config; fall through
                        finally
                        {
                            CryptographicOperations.ZeroMemory(mismatchKey);
                        }
                    }

                    if (mismatchDatabase == null)
                    {
                        var integrity = databaseService.VerifyConfigIntegrity(startupScope.Data);
                        switch (integrity)
                        {
                            case ConfigIntegrityResult.FirstRun:
                                database = new AppDatabase();
                                appInit.InitializeNewDatabase(database);
                                appInit.EnsureCurrentAccountCredential(credentialStore, database);
                                databaseService.SaveCredentialStoreAndConfig(credentialStore, database, startupScope.Data);
                                break;

                            case ConfigIntegrityResult.Valid:
                                database = databaseService.LoadConfig(startupScope.Data);
                                var currentSid = SidResolutionHelper.GetCurrentUserSid();
                                bool needsSave = appInit.EnsureCurrentAccountCredential(credentialStore, database);
                                appInit.EnsureInteractiveUserSidName(database);
                                needsSave |= appInit.NormalizeAccountSids(database.Apps, currentSid);
                                if (needsSave)
                                    databaseService.SaveCredentialStoreAndConfig(credentialStore, database, startupScope.Data);
                                break;

                            case ConfigIntegrityResult.DecryptionFailed:
                            default:
                            {
                                if (!startupUi.ConfirmStartFresh())
                                    return;

                                // Start Fresh: write empty config encrypted with current key
                                database = new AppDatabase();
                                appInit.InitializeNewDatabase(database);
                                appInit.EnsureCurrentAccountCredential(credentialStore, database);
                                databaseService.SaveCredentialStoreAndConfig(credentialStore, database, startupScope.Data);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Config loaded via mismatch path and re-saved above with current salt.
                        // Still run EnsureCurrentAccountCredential in case the account is missing.
                        database = mismatchDatabase;
                        var mismatchSave = appInit.EnsureCurrentAccountCredential(credentialStore, database);
                        appInit.EnsureInteractiveUserSidName(database);
                        if (mismatchSave)
                            databaseService.SaveCredentialStoreAndConfig(credentialStore, database, startupScope.Data);
                    }

                    // Validate no duplicate SIDs in credential store
                    var sidGroups = credentialStore.Credentials
                        .Where(c => !string.IsNullOrEmpty(c.Sid))
                        .GroupBy(c => c.Sid, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1);
                    foreach (var group in sidGroups)
                        log.Warn($"Duplicate credential SID detected: {group.Key} ({group.Count()} entries)");

                    // Apply logging setting from config (logging is always enabled during startup)
                    log.Enabled = database.Settings.EnableLogging;
                }
                // Key re-protected before runtime begins

                var session = new SessionContext
                {
                    Database = database,
                    CredentialStore = credentialStore,
                    PinDerivedKey = protectedKey,
                    LastPinVerifiedAt = DateTime.UtcNow
                };

                var options = new StartupOptions(isBackground);
                using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(foundationContainer, session, options);

                // Initialize license before MainForm — its constructor reads IsLicensed
                // for the title and About panel. Other init services run later in Start().
                sessionScope.Resolve<ILicenseService>().Initialize();

                var mainForm = sessionScope.Resolve<MainForm>();
                var lifecycleStarter = sessionScope.Resolve<AppLifecycleStarter>();
                var folderHandlerService = sessionScope.Resolve<IFolderHandlerService>();

                mainForm.PinDerivedKeyReplaced += (oldBuffer, newBuffer) =>
                {
                    oldBuffer.Dispose();
                    protectedKey = newBuffer;
                };

                lifecycleStarter.Start();

                Application.Run(mainForm);

                folderHandlerService.UnregisterAll();
            }
            finally
            {
                protectedKey.Dispose();
            }
        }
        finally
        {
            singleInstance.Dispose();
        }
    }
}
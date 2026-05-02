using System.Security.Cryptography;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.Startup;

/// <summary>
/// Foundation-scope service that orchestrates the full startup sequence from credential
/// loading through UI exit. <c>Program.Main</c> keeps process/bootstrap concerns
/// (exception logging, WinForms setup, elevation, privileges, mutex) and delegates
/// the nested session/config/UI lifecycle to this orchestrator.
///
/// Exit codes returned by <see cref="Run"/>:
/// <list type="bullet">
///   <item>0 — normal exit</item>
///   <item>-4 — credential load cancelled or failed</item>
/// </list>
/// Config decryption failure with Start Fresh rejected: returns 0 without entering the
/// session scope (caller exits normally; user is informed via <see cref="IStartupUI.ConfirmStartFresh"/>).
/// </summary>
public class StartupOrchestrator(
    ILoggingService log,
    IDatabaseService databaseService,
    IAppInitializationHelper appInit,
    IRememberPinService rememberPinService,
    IStartupUI startupUi,
    IStartupSessionScopeFactory sessionScopeFactory,
    IStartupMainFormRunner mainFormRunner,
    IStartupCredentialLoader credentialLoader)
{
    /// <summary>
    /// Runs the full startup sequence and returns the process exit code.
    /// </summary>
    /// <param name="isBackground">
    /// Whether the application was started with <c>--background</c>.
    /// </param>
    public int Run(bool isBackground, bool grantStartupRunAsUnlock = false)
    {
        using var identity = WindowsIdentity.GetCurrent();
        log.Info($"Running as: {identity.Name}");

        var credResult = credentialLoader.LoadAndVerifyCredentials();
        if (credResult == null)
            return -4;

        var protectedKey = new ProtectedBuffer(credResult.PinDerivedKey);
        try
        {
            var credentialStore = credResult.Store;
            var mismatchKey = credResult.MismatchKey;
            AppDatabase database;

            // Startup operations use a scoped unprotect, closed before Application.Run().
            using (var startupScope = protectedKey.Unprotect())
            {
                // Salt mismatch: load config encrypted under the mismatched (other-machine/session)
                // salt, re-save with the current session's salt, then zero the mismatch key.
                AppDatabase? mismatchDatabase = null;
                if (mismatchKey != null)
                {
                    try
                    {
                        mismatchDatabase = databaseService.LoadConfig(mismatchKey);
                        appInit.NormalizeAccountSids(mismatchDatabase.Apps, SidResolutionHelper.GetCurrentUserSid());
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
                                return 0;

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

                // Validate no duplicate SIDs in credential store.
                foreach (var group in credentialStore.Credentials
                             .Where(c => !string.IsNullOrEmpty(c.Sid))
                             .GroupBy(c => c.Sid, StringComparer.OrdinalIgnoreCase)
                             .Where(g => g.Count() > 1))
                {
                    log.Warn($"Duplicate credential SID detected: {group.Key} ({group.Count()} entries)");
                }

                // Apply logging setting from config (logging is always enabled during startup).
                log.Verbosity = database.Settings.LogVerbosity;
            }
            // Key re-protected before runtime begins.

            // Re-seal startkey.dat only after all credential/config work succeeded.
            if (!credResult.PinBypassed && rememberPinService.IsEnabled)
            {
                try
                {
                    rememberPinService.UpdateForPinChange(protectedKey);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to refresh Remember PIN key after successful PIN entry; disabling feature: {ex.Message}");
                    try
                    {
                        rememberPinService.Disable();
                    }
                    catch (Exception cleanupEx)
                    {
                        log.Warn($"Failed to clean up Remember PIN key material after startup refresh error: {cleanupEx.Message}");
                    }
                }
            }

            var session = new SessionContext
            {
                Database = database,
                CredentialStore = credentialStore,
                PinDerivedKey = protectedKey,
                LastPinVerifiedAt = DateTime.UtcNow
            };

            var options = new StartupOptions(isBackground, credResult.PinBypassed, grantStartupRunAsUnlock);
            using var sessionScope = sessionScopeFactory.BeginSessionScope(session, options);

            mainFormRunner.Run(sessionScope, (oldBuffer, newBuffer) =>
            {
                oldBuffer.Dispose();
                protectedKey = newBuffer;
            });
        }
        finally
        {
            protectedKey.Dispose();
        }

        return 0;
    }
}

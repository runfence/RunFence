using Autofac;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;
using RunFence.Persistence;
using RunFence.Startup;
using RunFence.Startup.UI;

namespace RunFence;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
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
                File.AppendAllText(PathConstants.LogFilePath, line);
            }
            catch
            {
            }
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

        var unlockRequested = args.Contains("--unlock", StringComparer.OrdinalIgnoreCase);
        var operationUnlockRequested = args.Contains("--unlock-operation", StringComparer.OrdinalIgnoreCase);
        var isBackground = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        var grantStartupRunAsUnlock = args.Contains("--startup-runas", StringComparer.OrdinalIgnoreCase);

        // Build the foundation container (pre-session). Side-effect-free — no I/O during Build().
        using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();

        var ipcClient = foundationContainer.Resolve<IIpcClient>();
        if (EarlyExitArgsHandler.HandleEarlyExitArgs(args, ipcClient))
            return 0;

        log = foundationContainer.Resolve<ILoggingService>();
        var configPaths = foundationContainer.Resolve<IConfigPaths>();
        var startupUi = foundationContainer.Resolve<IStartupUI>();

        SidResolutionHelper.InitializeInteractiveUserSid();
        if (!new ElevationChecker(startupUi).CheckElevation())
            return -1;

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
                     TokenPrivilegeHelper.SeTcbPrivilege,
                     TokenPrivilegeHelper.SeRelabelPrivilege,
                 })
        {
            try
            {
                TokenPrivilegeHelper.EnablePrivileges([priv]);
            }
            catch (Exception ex)
            {
                log.Warn($"Could not enable {priv} at startup: {ex.Message}");
            }
        }

        using var singleInstance = foundationContainer.Resolve<ISingleInstanceService>();
        var sessionAcquisitionHandler = foundationContainer.Resolve<SessionAcquisitionHandler>();
        if (unlockRequested || operationUnlockRequested)
        {
            var command = operationUnlockRequested ? IpcCommands.UnlockOperation : IpcCommands.Unlock;
            if (!sessionAcquisitionHandler.UnlockExistingInstance(log, command))
            {
                startupUi.ShowError("No suitable instance found in this session", "Unlock Failed");
                return -2;
            }

            return 0;
        }

        if (!sessionAcquisitionHandler
                .AcquireMutexOrTakeover(singleInstance, isBackground, log))
            return -3;

        var orchestrator = foundationContainer.Resolve<StartupOrchestrator>();
        return orchestrator.Run(isBackground, grantStartupRunAsUnlock);
    }
}

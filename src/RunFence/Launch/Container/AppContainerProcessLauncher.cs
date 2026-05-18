using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Launch.Container;

public class AppContainerProcessLauncher(
    ILoggingService log,
    IAppContainerEnvironmentSetup environmentSetup,
    IAppContainerProfileSetup appContainerProfileSetup,
    IAppContainerDataFolderService dataFolderService,
    IExplorerTokenProvider explorerTokenProvider,
    IAppContainerSidProvider sidProvider,
    IAppContainerTokenBuilder tokenBuilder,
    IAppContainerProcessStarter processStarter)
    : IAppContainerProcessLauncher
{
    public ProcessInfo LaunchFile(ProcessLaunchTarget target, AppContainerLaunchIdentity identity)
    {
        var psi = target;
        var entry = identity.Entry;

        log.Info($"AppContainerProcessLauncher: Starting launch of '{psi.ExePath}' in container '{entry.Name}'");

        IntPtr hExplorerToken = IntPtr.Zero;
        AppContainerLaunchTokenContext? tokenContext = null;
        RunFence.Launching.Environment.EnvironmentBlock? envBlock = null;

        try
        {
            hExplorerToken = explorerTokenProvider.GetExplorerToken();
            log.Info("AppContainerProcessLauncher: [1/6] Acquired interactive user token");

            var profileResult = appContainerProfileSetup.EnsureProfileUnderToken(entry, hExplorerToken);
            if (profileResult.Status != AppContainerProfileSetupStatus.Succeeded)
                throw new InvalidOperationException(profileResult.ErrorMessage ?? $"AppContainer profile setup failed for '{entry.Name}'.");

            var containerSidStr = sidProvider.GetSidString(entry.Name);
            dataFolderService.EnsureContainerDataFolder(entry, containerSidStr);
            dataFolderService.EnsureDataFolderTraverse(entry, containerSidStr);
            dataFolderService.EnsureInteractiveUserAccess(entry);

            log.Info($"AppContainerProcessLauncher: [2/6] Derived AppContainer SID: {containerSidStr}");

            tokenContext = tokenBuilder.Build(hExplorerToken, containerSidStr, entry.Capabilities);

            log.Info("AppContainerProcessLauncher: [3/6] Created AppContainer token via CreateAppContainerToken");
            log.Info("AppContainerProcessLauncher: [4/6] Set DACL");

            appContainerProfileSetup.TryEnableVirtualization(tokenContext.AppContainerToken);
            envBlock = environmentSetup.CreateLaunchEnvironment(hExplorerToken, entry, containerSidStr, psi.ExePath);

            envBlock.MergeInPlace(psi.EnvironmentVariables);

            log.Info("AppContainerProcessLauncher: [5/6] Built environment block with profile overrides");

            if (string.IsNullOrEmpty(psi.WorkingDirectory))
                psi = psi with { WorkingDirectory = Path.GetDirectoryName(psi.ExePath) };
            log.Info($"AppContainerProcessLauncher: [6/6] Calling CreateProcessWithTokenW");

            var pi = processStarter.Start(tokenContext.AppContainerToken, psi, envBlock.Pointer);
            try
            {
                _ = processStarter.GetImmediateExitCode(pi);

                log.Info($"AppContainerProcessLauncher: Launched '{psi.ExePath}' in container '{entry.Name}'");

                return new ProcessInfo(pi);
            }
            catch
            {
                CloseProcessInformation(pi);
                throw;
            }
        }
        finally
        {
            envBlock?.Dispose();
            tokenContext?.Dispose();
            if (hExplorerToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hExplorerToken);
        }
    }

    private static void CloseProcessInformation(ProcessLaunchNative.PROCESS_INFORMATION processInformation)
    {
        if (processInformation.hProcess != IntPtr.Zero)
            ProcessNative.CloseHandle(processInformation.hProcess);
        if (processInformation.hThread != IntPtr.Zero)
            ProcessNative.CloseHandle(processInformation.hThread);
    }
}

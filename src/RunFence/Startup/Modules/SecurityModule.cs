using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;

namespace RunFence.Startup.Modules;

public class SecurityModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<StartupSecurityScannerRunner>()
            .WithParameter("scannerPath", Path.Combine(AppContext.BaseDirectory, PathConstants.SecurityScannerExeName))
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<StartupSecurityService>()
            .As<IStartupSecurityService>()
            .SingleInstance();

        builder.RegisterType<WindowsHelloService>()
            .As<IWindowsHelloService>()
            .SingleInstance();
        builder.RegisterType<WindowsHelloNative>()
            .As<IWindowsHelloNative>()
            .SingleInstance();
        builder.RegisterType<WindowsHelloExecutionContext>()
            .As<IWindowsHelloExecutionContext>()
            .SingleInstance();

        builder.RegisterType<AutoLockTimerService>()
            .As<IAutoLockTimerService>()
            .SingleInstance();

        builder.RegisterType<UnlockProcessLauncher>()
            .As<IUnlockProcessLauncher>()
            .SingleInstance();
        builder.RegisterType<LockStateService>()
            .As<ILockStateService>()
            .SingleInstance();
        builder.RegisterType<CredentialUnlockService>()
            .As<ICredentialUnlockService>()
            .SingleInstance();
        builder.RegisterType<WindowsHelloPinFallbackPrompt>()
            .As<IWindowsHelloPinFallbackPrompt>()
            .As<IWindowsHelloPinFallbackPromptEventSource>()
            .SingleInstance();
        builder.RegisterType<WindowsHelloWindowHandleProvider>()
            .As<IWindowsHelloWindowHandleProvider>()
            .SingleInstance();
        builder.RegisterType<SecureDesktopPinPrompt>()
            .As<IUnlockPinPrompt>()
            .SingleInstance();

        builder.RegisterType<LockManager>()
            .WithParameter("operationUnlockTimeout", TimeSpan.FromMinutes(5))
            .As<ILockManager>()
            .As<ILockUiEventSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationState>()
            .As<IAppStateProvider>()
            .As<IAppLockControl>()
            .As<IDataChangeNotifier>()
            .As<IApplicationDataChangeSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ShortcutEnforcementHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<StartupEnforcementService>()
            .As<IStartupEnforcementService>()
            .SingleInstance();

        builder.RegisterType<PinChangeOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<InputInjectionBlockerService>()
            .As<IInputInjectionBlockerService>()
            .SingleInstance();

        builder.RegisterType<InputInjectionDisableBlockingDialogService>()
            .As<IInputInjectionDisableBlockingDialogService>()
            .SingleInstance();

        builder.RegisterType<InputInjectionBlockerController>()
            .AsSelf()
            .As<IRequiresInitialization>()
            .OrderBy(0)
            .SingleInstance();
    }
}
